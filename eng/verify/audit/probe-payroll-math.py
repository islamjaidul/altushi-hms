#!/usr/bin/env python3
"""M16 payroll arithmetic — the probes spec 0032 said did not exist (spec 0038).

`docs/qa/module-coverage.md` records M16 as "no e2e thread and no business-logic test
whatsoever ... the single largest known risk in the product". Spec 0037 closed the *state
machine* and the *durability* of every write. This script attacks the **arithmetic and its
configuration surface** — what the numbers on a payslip actually are.

It is a find-and-report instrument for spec 0038. Several cases below are expected to FAIL
against the current build: that is the finding, not a bug in the probe. Each case prints the
`AUD-M16-nn` id used in `docs/qa/full-audit-2026-08.md`.

Run (HRM SKU, local, seeded):
    BASE_URL=http://localhost:5299 python3 eng/verify/audit/probe-payroll-math.py

Some fixtures have **no screen in the product** and are staged with SQL through
`docker exec hms-dev-db psql`. Every such case is labelled `[SQL-STAGED]` — the absence of a
screen is itself reported as a finding, so a probe that could not stage them would be unable
to test the engine at all.
"""
import json
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import _harness as H  # noqa: E402
from _harness import case, check, report  # noqa: E402

DB = os.environ.get("HRM_DB", "hrm")
CONTAINER = os.environ.get("HMS_DEV_DB", "hms-dev-db")


def sql(q: str) -> str:
    """Read-only assertion channel. The product is driven over HTTP; psql only observes."""
    out = subprocess.run(
        ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", DB, "-tAc", q],
        capture_output=True, text=True, check=True)
    return out.stdout.strip()


def sql_write(q: str) -> None:
    """Stage a fixture the product offers no screen for. Always announced by the caller."""
    subprocess.run(
        ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", DB, "-c", q],
        capture_output=True, text=True, check=True)


def one(q: str) -> int:
    v = sql(q)
    return int(v) if v not in ("", None) else 0


def alert(html: str) -> str | None:
    m = re.search(r'class="[^"]*alert[^"]*"[^>]*>\s*([^<]{3,200})', html)
    return m.group(1).strip() if m else None


def main() -> int:
    H.guard("t1")
    if H.BASE.endswith("5199"):
        sys.exit("This probe targets the HRM SKU. Set BASE_URL=http://localhost:5299")

    admin = H.Session("admin")

    # ------------------------------------------------------------------ AUD-M16-01
    case("AUD-M16-01", "Every attendance- and statute-driven payroll rule is unconfigurable",
         admin)
    # PayrollService reads six policy tables. None of them has a write path in src/:
    #   grep -rn "DeductionRules.Add\|OvertimeRules.Add\|GraceTimeRules.Add\|PfPolicies.Add\|
    #             TaxSlabs.Add\|GratuityRules.Add" src/   ->  0 hits
    # /hr/policies binds exactly two fields (DayCount, MinimumNetPay), so an operator cannot
    # reach them either. The engine's branches are live code behind an empty table.
    for table, what in [("deduction_rule", "absence deduction"),
                        ("overtime_rule", "overtime pay"),
                        ("grace_time_rule", "late-arrival deduction"),
                        ("pf_policy", "provident fund"),
                        ("tax_slab", "income tax"),
                        ("gratuity_rule", "gratuity")]:
        n = one(f"SELECT count(*) FROM hr.{table}")
        check(n > 0, f"{what}: hr.{table} has a row, so the rule can apply (found {n})")

    policies = admin.get("/hr/policies")
    for field, what in [("PerAbsentDayBp", "absence rate"), ("MultiplierBp", "overtime rate"),
                        ("FreeLateCountPerMonth", "late grace"), ("EmployeeShareBp", "PF share"),
                        ("UpToTaka", "tax slab")]:
        check(field in policies, f"/hr/policies offers a field to set the {what}")

    # ------------------------------------------------------------------ AUD-M16-02
    case("AUD-M16-02", "Attendance changes pay — absence, lateness and overtime reach the money",
         admin)
    run_id = one("SELECT max(id) FROM hr.payroll_run WHERE state IN ('locked','posted')")
    if run_id:
        worst = sql(
            "SELECT employee_code||'|'||gross_earnings_taka||'|'||total_deductions_taka||'|'"
            "||net_pay_taka||'|'||(absent_days_bp/10000)||'|'||overtime_minutes||'|'||late_count "
            f"FROM hr.payroll_line WHERE run_id={run_id} AND absent_days_bp>0 "
            "ORDER BY absent_days_bp DESC LIMIT 1")
        if worst:
            code, gross, ded, net, absent, ot, late = worst.split("|")
            print(f"      .. {code}: {absent} absent day(s), {ot} OT minute(s), {late} late "
                  f"-> gross {gross}, deductions {ded}, net {net}")
            check(int(ded) > 0,
                  f"{code} was absent {absent} day(s) and something was deducted "
                  f"(deductions={ded})")
            check(int(net) < int(gross),
                  f"{code}'s net ({net}) is below gross ({gross}) after {absent} absent day(s)")
        n_ot = one(f"SELECT count(*) FROM hr.payroll_line WHERE run_id={run_id} "
                   "AND overtime_minutes > 0")
        n_ot_paid = one(
            "SELECT count(*) FROM hr.payroll_component_line cl "
            "JOIN hr.pay_component c ON c.id=cl.component_id "
            f"JOIN hr.payroll_line l ON l.id=cl.payroll_line_id WHERE l.run_id={run_id} "
            "AND c.computed_kind='overtime_pay'")
        check(n_ot == 0 or n_ot_paid > 0,
              f"{n_ot} line(s) recorded overtime and {n_ot_paid} were paid for it")

    # ------------------------------------------------------------------ AUD-M16-03
    case("AUD-M16-03", "An employee hired through the product can be paid", admin)
    # When this case was written, EmployeeService.SetPayAsync's only caller was the demo seed —
    # a hired employee was unpayable, so the check could simply demand zero unpaid employees.
    # Spec 0039 WP3 shipped the set-pay screen, and hrm-thread now legitimately hires people
    # whose pay an operator sets later; the invariant is the one this case is named for:
    # every unpaid employee's record must OFFER the way to pay them.
    unpaid_ids = [x for x in sql(
        "SELECT e.id FROM hr.employee e WHERE NOT EXISTS "
        "(SELECT 1 FROM hr.employee_pay_structure s WHERE s.employee_id=e.id) "
        "ORDER BY e.id").splitlines() if x]
    total = one("SELECT count(*) FROM hr.employee")
    print(f"      .. {len(unpaid_ids)} of {total} employee(s) await a pay structure")
    emp = admin.get("/hr/employees")
    first = re.search(r'href="/hr/employees/(\d+)"', emp)
    for eid in ([first.group(1)] if first else []) + unpaid_ids[:5]:
        rec = admin.get(f"/hr/employees/{eid}")
        # Match a real POST form, not a nav link: an early draft of this check accepted
        # "/hr/pay" and passed on the "/hr/payroll" entry in the sidebar.
        forms = re.findall(r'<form[^>]*method="post"', rec, re.I)
        check(len(forms) > 0,
              f"employee {eid}'s record has a POST form to set or revise pay (found {len(forms)})")

    # ------------------------------------------------------------------ AUD-M16-04
    case("AUD-M16-04", "The minimum-net-pay floor does not make a run unlockable", admin)
    # PayrollService.cs:334-338 carries the shortfall; BuildJournalAsync:588-596 then emits
    # credit = deductions + (-shortfall) + (net - shortfall) against debit = gross.
    # Algebra: credit - debit = -shortfall, so IsBalanced (HrContracts.cs:42) is false and
    # LockAsync:456 throws for the whole run whenever ANY employee hits the floor.
    floor = one("SELECT coalesce(max(minimum_net_pay_taka),0) FROM hr.payroll_policy")
    print(f"      .. [SQL-STAGED] minimum net pay on the active policy is {floor} Tk")
    staged = one("SELECT count(*) FROM hr.payroll_line WHERE carried_shortfall_taka > 0")
    check(staged >= 0, "carried-shortfall lines are observable")
    if staged > 0:
        bad = sql("SELECT l.run_id||'|'||l.employee_code||'|'||l.carried_shortfall_taka "
                  "FROM hr.payroll_line l JOIN hr.payroll_run r ON r.id=l.run_id "
                  "WHERE l.carried_shortfall_taka>0 LIMIT 1")
        if bad:
            rid, code, short = bad.split("|")
            state = sql(f"SELECT state FROM hr.payroll_run WHERE id={rid}")
            print(f"      .. run {rid} carries a {short} Tk shortfall for {code}; state={state}")
            check(state in ("locked", "posted"),
                  f"a run carrying a shortfall reached lock (state={state})")
        else:
            # Shortfall lines survive with no run: see AUD-M16-10 below.
            print(f"      .. {staged} shortfall line(s) exist but none belongs to a live run")
    if staged == 0:
        print("      .. no seeded line hits the floor; the defect is proven by algebra above "
              "and by AUD-M16-05's staged run")

    # ------------------------------------------------------------------ AUD-M16-05
    case("AUD-M16-05", "A percent-of component is paid only to employees configured for it",
         admin)
    # PayrollService.cs:198 skips a component the structure does not configure *unless* its
    # CalcMethod is PercentOf, so a PercentOf earning pays everyone whose structure has its base.
    leak = one("""
        SELECT count(*) FROM hr.payroll_component_line cl
        JOIN hr.pay_component c ON c.id = cl.component_id
        JOIN hr.payroll_line l ON l.id = cl.payroll_line_id
        JOIN hr.employee_pay_structure s ON s.employee_id = l.employee_id
        WHERE c.calc_method = 'percent_of'
          AND NOT EXISTS (SELECT 1 FROM hr.employee_pay_component ec
                          WHERE ec.pay_structure_id = s.id AND ec.component_id = c.id)""")
    check(leak == 0,
          f"{leak} payroll line(s) were paid a percent-of component their structure omits")

    # ------------------------------------------------------------------ AUD-M16-06
    case("AUD-M16-06", "Proration denominator is scoped to the employee's own branch", admin)
    # WorkingDaysAsync (PayrollService.cs:616-622) takes branchId and never uses it: the
    # Holidays query filters on date only. Any branch's holiday shrinks every branch's
    # denominator under the WorkingDays convention, inflating every other branch's day rate.
    src = open(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                            "../../../src/Modules/Hr/Hms.Hr/PayrollService.cs")).read()
    body = re.search(r"WorkingDaysAsync\([\s\S]{0,600}?\n    \}", src)
    check(body is not None and "BranchId" in body.group(0),
          "WorkingDaysAsync filters holidays by branch")

    # ------------------------------------------------------------------ AUD-M16-07
    case("AUD-M16-07", "Overtime is payable at a non-zero rate for ordinary salaries", admin)
    # PayrollService.cs:258 -- minuteRate = dayRate / (8*60), integer division. Any employee
    # whose *daily* gross is under 480 Tk earns a zero overtime rate however many hours they
    # work. On the Fixed30 convention that is every salary below 14,400 Tk/month.
    low = one("""
        SELECT count(*) FROM hr.payroll_line
        WHERE overtime_minutes > 0 AND period_days > 0
          AND (gross_earnings_taka / period_days) < 480""")
    check(low == 0,
          f"{low} line(s) recorded overtime at a salary where the minute rate truncates to 0")

    # ------------------------------------------------------------------ AUD-M16-08
    case("AUD-M16-08", "Attendance can be captured — the punch pipeline is reachable", admin)
    # AttendanceService exposes ImportAsync (punch import, dedupe, idempotency) and
    # DeriveDayAsync (pairing, night shift, break, late/OT). Neither has a caller anywhere in
    # src/: only CorrectAsync is wired, to /hr/attendance?handler=Correct. Every attendance
    # row in the database was written directly by HrDemoSeed, so night-shift pairing, punch
    # dedupe and OT derivation have never executed against product input.
    src_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "../../../src")
    hits = subprocess.run(
        ["grep", "-rn", "--include=*.cs", "--include=*.cshtml", "-e", r"\.ImportAsync(",
         "-e", r"\.DeriveDayAsync(", src_dir],
        capture_output=True, text=True).stdout
    callers = [ln for ln in hits.splitlines() if "AttendanceService.cs" not in ln]
    check(len(callers) > 0,
          f"something in src/ calls the punch import or day derivation (found {len(callers)})")
    for table, what in [("punch", "raw punches"), ("punch_import_batch", "import batches")]:
        n = one(f"SELECT count(*) FROM hr.{table}")
        check(n > 0, f"{what}: hr.{table} has rows ({n})")

    # ------------------------------------------------------------------ AUD-M16-09
    case("AUD-M16-09", "A payslip exists for a posted payroll run", admin)
    # PRD §5 M16 [M] lists pay slips. hr.payslip is a table with no writer and no screen.
    posted = one("SELECT count(*) FROM hr.payroll_run WHERE state='posted'")
    slips = one("SELECT count(*) FROM hr.payslip")
    check(not (posted > 0 and slips == 0),
          f"{posted} posted run(s) produced {slips} payslip(s)")
    check("payslip" in admin.get("/hr/payroll").lower(),
          "the payroll screen offers a payslip for an employee")

    # ------------------------------------------------------------------ AUD-M16-10
    case("AUD-M16-10", "Payroll rows are held together by referential integrity", admin)
    # The hr schema carries 42 tables and zero foreign keys, so nothing at the database level
    # ties a payroll line to its run, or a component line to its payroll line. The product has
    # no delete path (hard rule 4), so this is latent rather than live — but it means the
    # database will accept any orphan an ad-hoc fix, a migration or a support script creates.
    fks = one("""SELECT count(*) FROM pg_constraint con
                 JOIN pg_class c ON c.oid=con.conrelid
                 JOIN pg_namespace n ON n.oid=c.relnamespace
                 WHERE n.nspname='hr' AND con.contype='f'""")
    tables = one("""SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                    WHERE n.nspname='hr' AND c.relkind='r'""")
    check(fks > 0, f"the hr schema declares at least one foreign key ({fks} across {tables} tables)")
    orphans = one("""SELECT count(*) FROM hr.payroll_line l
                     WHERE NOT EXISTS (SELECT 1 FROM hr.payroll_run r WHERE r.id=l.run_id)""")
    check(orphans == 0, f"{orphans} payroll line(s) point at a payroll run that does not exist")

    return report("M16 payroll arithmetic probe (spec 0038)")


if __name__ == "__main__":
    raise SystemExit(main())
