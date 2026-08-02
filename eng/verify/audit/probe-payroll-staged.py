#!/usr/bin/env python3
"""M16 payroll — the three latent defects the seeded data never reaches (spec 0038).

`probe-payroll-math.py` reports what the shipped demo data proves. Three defects read in
`PayrollService.cs` are invisible there only because every seeded employee happens to be
configured the same way:

  AUD-M16-05  a PercentOf earning is paid to an employee whose structure omits it (:198)
  AUD-M16-07  the overtime minute rate integer-divides to zero below ~14,400 Tk/mo (:258)
  AUD-M16-04  the minimum-net-pay floor unbalances the journal, so Lock refuses the whole
              run for every employee because one hit the floor (:334-338 vs :588-596)

This script stages the missing configuration, then drives Generate → Review → Approve → Lock
**through the product over HTTP** and asserts the resulting rows.

**It rewrites one employee's pay structure and is local-only.** It restores what it changed on
the way out, but a run interrupted midway will leave that employee altered — which then shows up
as a false positive in `probe-payroll-math.py`'s AUD-M16-05 count. If the two disagree, reseed:

    docker exec hms-dev-db psql -U postgres -d postgres \
      -c "DROP DATABASE IF EXISTS hrm WITH (FORCE);" -c "CREATE DATABASE hrm;"   # then restart the host

    BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-staged.py

[SQL-STAGED] marks every fixture the product offers no screen for — six payroll policy
tables and the employee pay structure itself have no write path anywhere in src/ (that is
AUD-M16-01 and AUD-M16-03). Staging them is the only way to reach the engine at all.
"""
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import _harness as H  # noqa: E402
from _harness import case, check, report  # noqa: E402

CONTAINER = os.environ.get("HMS_DEV_DB", "hms-dev-db")
DB = os.environ.get("HRM_DB", "hrm")
PERIOD = os.environ.get("PROBE_PERIOD", "01/12/2026")   # a period with no run yet
PERIOD_SQL = "2026-12-01"


def psql(q: str, tuples_only: bool = True) -> str:
    args = ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", DB]
    args += ["-tAc", q] if tuples_only else ["-c", q]
    r = subprocess.run(args, capture_output=True, text=True)
    if r.returncode != 0:
        raise RuntimeError(f"psql failed: {r.stderr.strip()}\n  query: {q[:200]}")
    return r.stdout.strip()


def one(q: str) -> int:
    v = psql(q)
    return int(v) if v else 0


def delete_run(where: str) -> None:
    """Remove a staged run children-first. Spec 0039 WP3 gave the hr schema real foreign keys
    (payroll_line -> payroll_run, ON DELETE RESTRICT), so the bare DELETE this probe used when
    the schema had none is now refused by the database — which is exactly the integrity the
    probe asked for in AUD-M16-10."""
    runs = f"SELECT id FROM hr.payroll_run WHERE {where}"
    psql(f"DELETE FROM hr.payslip WHERE payroll_line_id IN "
         f"(SELECT id FROM hr.payroll_line WHERE run_id IN ({runs}))")
    psql(f"DELETE FROM hr.payroll_component_line WHERE run_id IN ({runs})")
    psql(f"DELETE FROM hr.payroll_line WHERE run_id IN ({runs})")
    psql(f"DELETE FROM hr.payroll_run WHERE {where}")


def alert(html: str) -> str | None:
    m = re.search(r'class="[^"]*alert[^"]*"[^>]*>\s*([^<]{3,300})', html)
    return m.group(1).strip() if m else None


def post(s: H.Session, path: str, fields: dict, page: str | None = None):
    return s.post(path, fields, page if page is not None else s.get("/hr/payroll"))


def main() -> int:
    H.guard("t1")
    if not H.is_local():
        sys.exit("REFUSED: this probe stages fixtures with SQL and is local-only.")
    if H.BASE.endswith("5199"):
        sys.exit("This probe targets the HRM SKU. Set BASE_URL=http://localhost:5299")

    admin = H.Session("admin")
    branch = one("SELECT min(branch_id) FROM hr.employee")

    # ---------------------------------------------------------------- staging
    print("\n[SQL-STAGED] installing this probe's own overtime and deduction rules")
    delete_run(f"period='{PERIOD_SQL}'")

    # Since spec 0039 WP3 the product seeds and screens these rules, and deduction_rule carries
    # a GiST no-overlap exclusion — so the probe's rules can no longer simply INSERT alongside
    # an open demo rule. Close any open rows for the staging window and reopen them on the way
    # out, so the probe still controls the exact rates its arithmetic below assumes.
    for table in ("deduction_rule", "overtime_rule"):
        psql(f"UPDATE hr.{table} SET effective_to='2025-12-31' "
             "WHERE effective_to IS NULL AND effective_from < '2026-01-01'")

    # A low-paid employee with overtime: 12,000 Tk basic -> day rate 400 -> minuteRate 0.
    low = one("""
        SELECT s.employee_id FROM hr.employee_pay_structure s
        JOIN hr.employee_pay_component c ON c.pay_structure_id=s.id AND c.component_id=1
        WHERE s.effective_to IS NULL ORDER BY c.amount_taka ASC LIMIT 1""")
    basic_before = one(f"""SELECT ec.amount_taka FROM hr.employee_pay_component ec
                           JOIN hr.employee_pay_structure s ON s.id=ec.pay_structure_id
                           WHERE s.employee_id={low} AND s.effective_to IS NULL
                             AND ec.component_id=1""")
    floor_before = one("SELECT minimum_net_pay_taka FROM hr.payroll_policy "
                       "WHERE id=(SELECT max(id) FROM hr.payroll_policy)")
    psql(f"""UPDATE hr.employee_pay_component SET amount_taka=12000
             WHERE component_id=1 AND pay_structure_id=(
               SELECT id FROM hr.employee_pay_structure
               WHERE employee_id={low} AND effective_to IS NULL)""")
    # Strip that person's HRA so the PercentOf leak has a subject.
    psql(f"""DELETE FROM hr.employee_pay_component WHERE component_id=2 AND pay_structure_id=(
               SELECT id FROM hr.employee_pay_structure
               WHERE employee_id={low} AND effective_to IS NULL)""")
    # Give them overtime and an absence inside the probe period.
    psql(f"DELETE FROM hr.attendance_day WHERE employee_id={low} "
         f"AND on_date BETWEEN '{PERIOD_SQL}' AND '2026-12-31'")
    psql(f"""INSERT INTO hr.attendance_day
             (branch_id, employee_id, on_date, status, payable_fraction_bp, late_minutes,
              early_out_minutes, overtime_minutes, worked_minutes, corrected, derived_at)
             VALUES ({branch},{low},'2026-12-02','present',10000,0,0,300,780,false,now()),
                    ({branch},{low},'2026-12-03','absent',0,0,0,0,0,false,now()),
                    ({branch},{low},'2026-12-04','absent',0,0,0,0,0,false,now())""")
    # The rules the engine reads and nothing writes.
    psql(f"""INSERT INTO hr.overtime_rule
             (branch_id, effective_from, threshold_minutes, multiplier_bp,
              max_minutes_per_month, bank_instead_of_pay, created_at, created_by)
             VALUES ({branch},'2026-01-01',0,20000,0,false,now(),1)""")
    psql(f"""INSERT INTO hr.deduction_rule
             (branch_id, effective_from, per_absent_day_bp, per_leave_without_pay_day_bp,
              created_at, created_by)
             VALUES ({branch},'2026-01-01',10000,10000,now(),1)""")
    print(f"      .. employee {low}: basic 12,000 Tk, HRA removed, 300 OT minutes, 2 absences")
    print("      .. overtime rule 2.0x from 0 minutes; absence deduction 1.0 day-rate per day")

    # ---------------------------------------------------------------- generate
    case("AUD-M16-STAGED", f"Payroll for {PERIOD} computes through the product", admin)
    pay = admin.get("/hr/payroll")
    url, body = post(admin, "/hr/payroll?handler=Generate", {"Period": PERIOD}, pay)
    check("/denied" not in url, f"Generate was accepted ({alert(body)!r})")
    rid = one(f"SELECT id FROM hr.payroll_run WHERE period='{PERIOD_SQL}'")
    check(rid > 0, f"a run exists for the period (id={rid})")
    if not rid:
        return report("M16 staged payroll probe (spec 0038)")

    line = psql(f"""SELECT gross_earnings_taka||'|'||total_deductions_taka||'|'||net_pay_taka
                    ||'|'||carried_shortfall_taka||'|'||overtime_minutes||'|'||period_days
                    FROM hr.payroll_line WHERE run_id={rid} AND employee_id={low}""")
    gross, ded, net, short, ot, days = (int(x) for x in line.split("|"))
    print(f"      .. gross {gross}, deductions {ded}, net {net}, "
          f"shortfall {short}, OT minutes {ot}")

    # ------------------------------------------------------------ AUD-M16-07
    case("AUD-M16-07", "Overtime at a 12,000 Tk salary pays a non-zero amount", admin)
    ot_paid = one(f"""SELECT coalesce(sum(cl.amount_taka),0) FROM hr.payroll_component_line cl
                      JOIN hr.pay_component c ON c.id=cl.component_id
                      JOIN hr.payroll_line l ON l.id=cl.payroll_line_id
                      WHERE l.run_id={rid} AND l.employee_id={low}
                        AND c.computed_kind='overtime_pay'""")
    # The engine pays OT from the day rate of the non-OT earnings. `gross` includes the OT
    # line itself, so the base must subtract what was paid — an expectation computed from
    # gross chases its own answer and drifts red on a database where other earnings exist.
    base = gross - ot_paid
    expected = round(base / days / (8 * 60) * 300 * 2) if days else 0
    print(f"      .. OT base {base} Tk over {days} day(s); 300 min at 2.0x should pay about {expected} Tk")
    # The pre-fix rate was truncated to whole taka per MINUTE (:258), not rounded at the end,
    # so the error is proportionally huge at ordinary Bangladeshi salaries and reaches 100%
    # below a 480 Tk day rate. Allow 2% for legitimate whole-taka rounding; worse is the bug.
    slack = max(2, expected * 2 // 100)
    check(abs(ot_paid - expected) <= slack,
          f"300 overtime minutes paid {ot_paid} Tk against {expected} Tk due "
          f"(a whole-taka minute rate would pay {(base // days // 480) * 600 if days else 0} Tk)")

    # ------------------------------------------------------------ AUD-M16-05
    case("AUD-M16-05", "A percent-of component is paid only where it is configured", admin)
    hra = one(f"""SELECT coalesce(sum(cl.amount_taka),0) FROM hr.payroll_component_line cl
                  JOIN hr.payroll_line l ON l.id=cl.payroll_line_id
                  WHERE l.run_id={rid} AND l.employee_id={low} AND cl.component_id=2""")
    configured = one(f"""SELECT count(*) FROM hr.employee_pay_component ec
                         JOIN hr.employee_pay_structure s ON s.id=ec.pay_structure_id
                         WHERE s.employee_id={low} AND s.effective_to IS NULL
                           AND ec.component_id=2""")
    print(f"      .. HRA configured for this employee: {configured}; HRA paid: {hra} Tk")
    check(hra == 0 or configured > 0,
          f"HRA paid {hra} Tk to an employee whose pay structure does not include it")

    # ------------------------------------------------------------ AUD-M16-04
    case("AUD-M16-04", "The minimum-net-pay floor does not make the run unlockable", admin)
    # Drive the floor: raise it above this employee's net so a shortfall is carried.
    if short == 0 and net > 0:
        # Scope the raise to the policy the run actually resolved: an unscoped UPDATE here
        # rewrites every branch's floor and does not restore on cleanup.
        psql(f"""UPDATE hr.payroll_policy SET minimum_net_pay_taka={net + 1000}
                 WHERE id = (SELECT max(id) FROM hr.payroll_policy)""")
        delete_run(f"id={rid}")
        post(admin, "/hr/payroll?handler=Generate", {"Period": PERIOD})
        rid = one(f"SELECT id FROM hr.payroll_run WHERE period='{PERIOD_SQL}'")
        short = one(f"SELECT coalesce(max(carried_shortfall_taka),0) FROM hr.payroll_line "
                    f"WHERE run_id={rid}")
        print(f"      .. [SQL-STAGED] floor raised to {net + 1000} Tk; "
              f"carried shortfall now {short} Tk")
    check(short > 0, f"the run carries a shortfall to test the floor with ({short} Tk)")

    pay = admin.get("/hr/payroll")
    for handler, label in [("Review", "reviewed"), ("RequestApproval", "sent for approval"),
                           ("Approve", "approved")]:
        url, body = post(admin, f"/hr/payroll?handler={handler}", {"id": rid}, pay)
        state = psql(f"SELECT state FROM hr.payroll_run WHERE id={rid}")
        print(f"      .. {handler} -> state={state}")
        pay = admin.get("/hr/payroll")
    url, body = post(admin, "/hr/payroll?handler=Lock", {"id": rid}, pay)
    state = psql(f"SELECT state FROM hr.payroll_run WHERE id={rid}")
    msg = alert(body) or next(
        (t.strip() for t in re.findall(r">([^<>]{20,300})<", body)
         if "journal" in t.lower() or "balance" in t.lower()), "")
    print(f"      .. Lock -> state={state}; message={msg!r}")
    check(state == "locked",
          f"a run carrying a {short} Tk shortfall locks (state={state}, message={msg!r})")

    # ---------------------------------------------------------------- restore
    # Put back what was staged. Without this the altered pay structure makes
    # probe-payroll-math.py's AUD-M16-05 count every historical run's HRA line a leak.
    print("\n[SQL-STAGED] restoring the staged fixtures")
    psql(f"""INSERT INTO hr.employee_pay_component
             (pay_structure_id, component_id, amount_taka)
             SELECT id, 2, {basic_before // 2} FROM hr.employee_pay_structure
             WHERE employee_id={low} AND effective_to IS NULL
             AND NOT EXISTS (SELECT 1 FROM hr.employee_pay_component ec
                             WHERE ec.pay_structure_id=hr.employee_pay_structure.id
                               AND ec.component_id=2)""")
    psql(f"""UPDATE hr.employee_pay_component SET amount_taka={basic_before}
             WHERE component_id=1 AND pay_structure_id=(
               SELECT id FROM hr.employee_pay_structure
               WHERE employee_id={low} AND effective_to IS NULL)""")
    psql(f"DELETE FROM hr.attendance_day WHERE employee_id={low} "
         f"AND on_date BETWEEN '{PERIOD_SQL}' AND '2026-12-31'")
    # Only the probe's own rules go; the demo's rules reopen exactly as they were staged shut.
    psql("DELETE FROM hr.overtime_rule WHERE effective_from='2026-01-01'")
    psql("DELETE FROM hr.deduction_rule WHERE effective_from='2026-01-01'")
    for table in ("deduction_rule", "overtime_rule"):
        psql(f"UPDATE hr.{table} SET effective_to=NULL WHERE effective_to='2025-12-31'")
    delete_run(f"period='{PERIOD_SQL}'")
    psql(f"UPDATE hr.payroll_policy SET minimum_net_pay_taka={floor_before} "
         "WHERE id=(SELECT max(id) FROM hr.payroll_policy)")
    print("      .. pay structure, attendance, rules, run and floor restored")

    return report("M16 staged payroll probe (spec 0038)")


if __name__ == "__main__":
    raise SystemExit(main())
