#!/usr/bin/env python3
"""Handler-guarded-in-body authorization seams (spec 0038, find-and-report).

Razor Pages allows exactly one `[Authorize]` per PageModel, so a screen that shows a folio and
also *posts money to it* carries the **read** policy on the class and defends every write with an
`if (!CanX) return Forbid();` on the first line of the handler. That line is the whole control.
If it is missing, wrong, or checks a permission the button does not, a user who legitimately holds
the page's read grant can drive a state change nobody meant to give them — and every automated
check still passes, because the *page* is correctly protected and the *button* is correctly hidden.
Hiding a button is never the control (G10).

This probe walks every such seam the audit named. For each one it:

  1. logs in as a user who **holds the page's read permission and not the write permission**,
  2. GETs the page and asserts **200** — proving the actor is legitimately on the screen, so any
     later refusal came from the handler and not from the page policy,
  3. takes a fingerprint of every mutable table (counts + a state digest, straight from psql),
  4. POSTs the handler with plausible field values and a valid antiforgery token,
  5. asserts the POST was refused **and** that the fingerprint is byte-identical afterwards.

Refusal has two honest shapes in this codebase and the probe distinguishes them, because a bare
status code cannot: `Forbid()` under cookie auth is a **302 to /denied**, never a 403, while the
two handlers that reload the page instead answer **200 with a refusal message**. Both are real
refusals. Only an accepted POST — or a changed fingerprint — is a finding.

Nothing here is deleted and nothing is approved: every POST is expected to bounce, and the two
directions an actor *is* entitled to are deliberately left un-posted rather than exercised.

Run (both hosts up, seeded):
    python3 eng/verify/audit/probe-authz-seams.py
    BASE_URL=http://localhost:5199 HRM_URL=http://localhost:5299 \
        python3 eng/verify/audit/probe-authz-seams.py
"""
import os
import re
import subprocess
import sys
import urllib.error

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import _harness as H  # noqa: E402
from _harness import case, check, report  # noqa: E402

ERP = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
HRM = os.environ.get("HRM_URL", "http://localhost:5299").rstrip("/")
CONTAINER = os.environ.get("HMS_DEV_DB", "hms-dev-db")
ERP_DB = os.environ.get("HMS_DB", "hms")
HRM_DB = os.environ.get("HRM_DB", "hrm")

# The two HR personas the shared CAST omits — role labels only, so the run log reads properly.
H.CAST.setdefault("farid", "HR Officer")
H.CAST.setdefault("shirin", "Department Head")
H.CAST.setdefault("kamal", "Accounts Manager")


def sql(db: str, q: str) -> str:
    """Read-only assertion channel. The product is driven over HTTP; psql only observes."""
    out = subprocess.run(
        ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", db, "-tAc", q],
        capture_output=True, text=True, check=True)
    return out.stdout.strip()


# Every table an IPD / EMR / LIS / radiology write could land in, plus the two ledgers that would
# record it. A per-seam query would have to be right about which table each handler touches; a
# whole-schema fingerprint is right by construction, and catches a write that lands somewhere
# unexpected — which is exactly the case an authorization bypass produces.
ERP_FINGERPRINT = """
SELECT concat_ws('|',
  (SELECT count(*) FROM bill.charge_line), (SELECT count(*) FROM bill.invoice),
  (SELECT count(*) FROM bill.receipt),     (SELECT count(*) FROM bill.due),
  (SELECT count(*) FROM ipd.folio),        (SELECT count(*) FROM ipd.indent),
  (SELECT count(*) FROM ipd.indent_item),  (SELECT count(*) FROM ipd.bed_day),
  (SELECT count(*) FROM ipd.bed_stay),     (SELECT count(*) FROM ipd.certificate),
  (SELECT count(*) FROM diag.test_order),  (SELECT count(*) FROM diag.order_test),
  (SELECT count(*) FROM emr.note),         (SELECT count(*) FROM lis.sample_test),
  (SELECT count(*) FROM kernel.approval_request),
  (SELECT count(*) FROM kernel.audit_event),
  (SELECT count(*) FROM pharm.stock_move),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM ipd.admission),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM ipd.bed),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM lis.sample),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM radiology.study),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM emr.note))
"""

HRM_FINGERPRINT = """
SELECT concat_ws('|',
  (SELECT count(*) FROM hr.payroll_run),   (SELECT count(*) FROM hr.payroll_line),
  (SELECT count(*) FROM hr.payslip),       (SELECT count(*) FROM hr.employee_ledger_entry),
  (SELECT count(*) FROM hr.leave_application),
  (SELECT count(*) FROM hr.employee_pay_structure),
  (SELECT count(*) FROM kernel.approval_request),
  (SELECT count(*) FROM kernel.audit_event),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM hr.payroll_run),
  (SELECT md5(string_agg(id||':'||state, ',' ORDER BY id)) FROM hr.leave_application))
"""


def fingerprint(db: str) -> str:
    return sql(db, ERP_FINGERPRINT if db == ERP_DB else HRM_FINGERPRINT)


def use(base: str) -> None:
    """Point the harness at a host. Two SKUs, one probe — sessions are built after the switch."""
    H.BASE = base


TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')


def attempt(sess, path: str, fields: dict, token_html: str) -> tuple[str, str, int]:
    """POST `path` with a valid antiforgery token; return (final url, body, status).

    The token is bound to the caller's cookie, not to the page it was rendered on, so a seam
    whose *page* the actor cannot open is still tested with a genuine token rather than being
    rejected as a forgery — otherwise a page-policy denial would masquerade as a handler denial.
    """
    data = dict(fields)
    m = TOKEN_RE.search(token_html)
    if m:
        data["__RequestVerificationToken"] = m.group(1)
    try:
        url, body = sess.post(path, data, token_html)
        return url, body, 200
    except urllib.error.HTTPError as e:
        return e.geturl(), "", e.code


def seam(sess, db: str, page: str, handler: str, fields: dict,
         token_page: str | None = None, inline: str | None = None) -> bool:
    """One handler-guarded-in-body seam. True when the write was genuinely refused.

    `inline` names the refusal wording of a handler that answers 200-with-a-message instead of
    `Forbid()`. Both shapes are real refusals; an accepted POST is not.
    """
    code, _ = sess.status(page)
    on_page = code == 200
    token_html = sess.get(token_page or page)
    before = fingerprint(db)
    path = f"{page}{'&' if '?' in page else '?'}handler={handler}"
    url, body, status = attempt(sess, path, fields, token_html)
    after = fingerprint(db)

    if status in (401, 403):
        how = f"{status}"
    elif "/denied" in url or "/login" in url:
        how = "302 -> /denied"
    elif inline and inline.lower() in body.lower():
        how = "200 + refusal message"
    else:
        how = "ACCEPTED"

    where = "handler" if on_page else "page policy"
    ok = check(how != "ACCEPTED",
               f"{page}?handler={handler} refused for {sess.user} ({where}: {how})")
    ok &= check(before == after, f"  .. nothing changed in the database after that POST")
    return ok


def main() -> int:
    H.guard("t1")
    print(f"ERP {ERP} (db {ERP_DB})   HRM {HRM} (db {HRM_DB})")

    # ---------------------------------------------------------------- ERP fixtures
    use(ERP)
    admission = sql(ERP_DB, "SELECT max(id) FROM ipd.admission") or "1"
    bed = sql(ERP_DB, "SELECT min(id) FROM ipd.bed") or "1"
    other_bed = sql(ERP_DB, "SELECT max(id) FROM ipd.bed") or "2"
    note = sql(ERP_DB, "SELECT max(id) FROM emr.note") or "1"
    sample = sql(ERP_DB, "SELECT min(id) FROM lis.sample") or "1"
    study = sql(ERP_DB, "SELECT max(id) FROM radiology.study") or "1"
    service = sql(ERP_DB, "SELECT min(id) FROM adm.service") or "1"
    test = sql(ERP_DB, "SELECT min(id) FROM adm.test_catalog") or "1"
    product = sql(ERP_DB, "SELECT min(id) FROM pharm.product") or "1"
    print(f"fixtures: admission={admission} bed={bed} note={note} sample={sample} study={study}")

    chowdhury = H.Session("chowdhury")   # ipd.read, emr.read, lis.worklist.read — no write grant
    nasrin = H.Session("nasrin")         # emr.read + ipd.service.post — no note.write/settle/manage
    farhana = H.Session("farhana")       # lis + radiology READ; no sample.collect, no study.perform

    # ------------------------------------------------------------------ AUD-AUZ-01
    case("AUD-AUZ-01", "A reader cannot supersede a prescription (emr.read page, note.write write)",
         nasrin)
    check(nasrin.status(f"/emr/prescription/{note}")[0] == 200,
          f"nasrin (emr.read, no emr.note.write) can open /emr/prescription/{note}")
    seam(nasrin, ERP_DB, f"/emr/prescription/{note}", "Correct", {"Id": note},
         inline="Only a prescriber")

    # ------------------------------------------------------------------ AUD-AUZ-02
    case("AUD-AUZ-02", "IPD folio: all nine money/state handlers refuse a read-only consultant",
         chowdhury)
    folio = f"/ipd/folio/{admission}"
    check(chowdhury.status(folio)[0] == 200,
          f"chowdhury (ipd.read only) can open {folio} — so refusals below are handler-level")
    base = {"AdmissionId": admission}
    seam(chowdhury, ERP_DB, folio, "Service",
         {**base, "ServiceId": service, "Qty": "1"})
    seam(chowdhury, ERP_DB, folio, "Indent",
         {**base, "ProductIds": [product], "ItemQtys": ["1"], "IndentNote": "audit probe"})
    seam(chowdhury, ERP_DB, folio, "ReturnItem",
         {**base, "IndentId": "1", "ReturnProductId": product, "ReturnQty": "1"})
    seam(chowdhury, ERP_DB, folio, "Advance",
         {**base, "AdvanceAmount": "500", "AdvanceTender": "cash"})
    seam(chowdhury, ERP_DB, folio, "OrderTests", {**base, "TestIds": [test]})
    seam(chowdhury, ERP_DB, folio, "Transfer", {**base, "ToBedId": other_bed})
    seam(chowdhury, ERP_DB, folio, "Death", dict(base))
    seam(chowdhury, ERP_DB, folio, "Absconded", dict(base))
    seam(chowdhury, ERP_DB, folio, "RaiseLatePost", {**base, "Reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-03
    case("AUD-AUZ-03", "IPD folio: a nurse who may post services still cannot settle or transfer",
         nasrin)
    # nasrin holds ipd.service.post. The four below need ipd.settle or ipd.manage, which she does
    # not have — the seam is that they all live on the same page as the one she may use. Her own
    # permitted handlers are deliberately not posted: this probe reports, it does not write.
    check(nasrin.status(folio)[0] == 200, f"nasrin (ipd.service.post) can open {folio}")
    seam(nasrin, ERP_DB, folio, "Advance", {**base, "AdvanceAmount": "500", "AdvanceTender": "cash"})
    seam(nasrin, ERP_DB, folio, "Transfer", {**base, "ToBedId": other_bed})
    seam(nasrin, ERP_DB, folio, "Death", dict(base))
    seam(nasrin, ERP_DB, folio, "Absconded", dict(base))

    # ------------------------------------------------------------------ AUD-AUZ-04
    case("AUD-AUZ-04", "Discharge: the six handlers that close a stay refuse a read-only consultant",
         chowdhury)
    disch = f"/ipd/discharge/{admission}"
    check(chowdhury.status(disch)[0] == 200, f"chowdhury can open {disch}")
    seam(chowdhury, ERP_DB, disch, "Initiate",
         {**base, "ClinicalSummary": "audit probe — must not be accepted"})
    seam(chowdhury, ERP_DB, disch, "Prepare", dict(base))
    seam(chowdhury, ERP_DB, disch, "Clear", dict(base))
    seam(chowdhury, ERP_DB, disch, "Confirm",
         {**base, "DiscountFlat": "0", "DiscountReason": ""})
    seam(chowdhury, ERP_DB, disch, "Discharge",
         {**base, "OutstandingReason": "audit probe"})
    seam(chowdhury, ERP_DB, disch, "Reopen", {**base, "Reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-05
    case("AUD-AUZ-05", "Bed board: housekeeping transitions refuse a reader", chowdhury)
    check(chowdhury.status("/ipd/board")[0] == 200, "chowdhury can open /ipd/board")
    for handler in ("CleaningDone", "OutOfService", "ReturnToService"):
        seam(chowdhury, ERP_DB, "/ipd/board", handler,
             {"BedId": bed, "Reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-06
    case("AUD-AUZ-06", "Admissions: reservation and the R4 dues block refuse a reader", chowdhury)
    check(chowdhury.status("/ipd/admissions")[0] == 200, "chowdhury can open /ipd/admissions")
    for handler in ("ConfirmReservation", "CancelReservation", "RaiseBlock",
                    "ApplyBlock", "RaiseRelease", "ApplyRelease"):
        seam(chowdhury, ERP_DB, "/ipd/admissions", handler,
             {"AdmissionId": admission, "Reason": "audit probe", "ApprovalId": "1"})

    # ------------------------------------------------------------------ AUD-AUZ-07
    case("AUD-AUZ-07", "Radiology worklist: 'study done' sits on a read policy", farhana)
    # The page policy is radiology.worklist.read, which the reporting pathologist holds so she can
    # read her list. Marking a study performed bills film and closes the modality's queue entry.
    check(farhana.status("/radiology/worklist")[0] == 200,
          "farhana (radiology.worklist.read + report.write, no study.perform) can open the worklist")
    seam(farhana, ERP_DB, "/radiology/worklist", "Done",
         {"StudyId": study, "FilmSize": "8x10", "FilmCount": "2", "Note": "audit probe"},
         inline="Only a radiology technician")

    # ------------------------------------------------------------------ AUD-AUZ-08
    case("AUD-AUZ-08", "LIS board: sample custody refuses a consultant who may only read it",
         chowdhury)
    check(chowdhury.status("/lis/board")[0] == 200,
          "chowdhury (lis.worklist.read only) can open /lis/board")
    for handler in ("Collect", "Receive", "Reject"):
        seam(chowdhury, ERP_DB, "/lis/board", handler,
             {"sampleId": sample, "reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-09
    case("AUD-AUZ-09", "LIS board: a pathologist who may enter results cannot take custody",
         farhana)
    check(farhana.status("/lis/board")[0] == 200,
          "farhana (lis.result.enter + verify, no lis.sample.collect) can open /lis/board")
    for handler in ("Collect", "Receive", "Reject"):
        seam(farhana, ERP_DB, "/lis/board", handler,
             {"sampleId": sample, "reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-10
    # The ERP host ships the HR screens too, and its cast carries the two HR personas the shared
    # CAST omits. Their grants are the point: shirin recommends leave and never decides it.
    case("AUD-AUZ-10", "ERP host: a department head can recommend leave but not decide it",
         "shirin")
    shirin = H.Session("shirin")
    check(shirin.status("/hr/leave")[0] == 200, "shirin (hr.read + hr.leave.recommend) opens /hr/leave")
    seam(shirin, ERP_DB, "/hr/leave", "Decide",
         {"id": "1", "approve": "true", "note": "audit probe"}, token_page="/hr/leave")
    check(shirin.denied("/hr/payroll"),
          "shirin has no hr.payroll.run, so /hr/payroll is refused at the page policy")
    seam(shirin, ERP_DB, "/hr/payroll", "Generate", {"Period": "01/03/2026"},
         token_page="/hr/leave")

    # ------------------------------------------------------------------ AUD-AUZ-11
    case("AUD-AUZ-11", "ERP host: the HR officer who runs payroll cannot approve it", "farid")
    farid_erp = H.Session("farid")
    check(farid_erp.status("/hr/payroll")[0] == 200,
          "farid (hr.payroll.run) opens /hr/payroll — refusals below are handler-level")
    seam(farid_erp, ERP_DB, "/hr/payroll", "Approve", {"id": "1"})
    seam(farid_erp, ERP_DB, "/hr/payroll", "Reverse", {"id": "1", "reason": "audit probe"})
    seam(farid_erp, ERP_DB, "/hr/leave", "Recommend", {"id": "1"})

    # ---------------------------------------------------------------- HRM host
    use(HRM)
    run_id = sql(HRM_DB, "SELECT max(id) FROM hr.payroll_run") or "1"
    posted = sql(HRM_DB, "SELECT max(id) FROM hr.payroll_run WHERE state='posted'") or run_id
    leave_id = (sql(HRM_DB, "SELECT max(id) FROM hr.leave_application WHERE state='applied'")
                or sql(HRM_DB, "SELECT max(id) FROM hr.leave_application") or "1")
    print(f"\nHRM fixtures: payroll run={run_id} posted={posted} leave={leave_id}")

    # ------------------------------------------------------------------ AUD-AUZ-12
    case("AUD-AUZ-12", "HRM: the officer who runs payroll cannot approve or reverse it", "farid")
    farid = H.Session("farid")
    check(farid.status("/hr/payroll")[0] == 200, "farid (hr.payroll.run) opens /hr/payroll")
    seam(farid, HRM_DB, "/hr/payroll", "Approve", {"id": run_id})
    seam(farid, HRM_DB, "/hr/payroll", "Reverse", {"id": posted, "reason": "audit probe"})

    # ------------------------------------------------------------------ AUD-AUZ-13
    case("AUD-AUZ-13", "HRM: a department head cannot drive any payroll transition", "nasrin")
    dept = H.Session("nasrin")           # Department Head on the HRM SKU (shirin's ERP twin)
    check(dept.denied("/hr/payroll"),
          "nasrin (hr.read, no hr.payroll.run) is refused /hr/payroll at the page policy")
    for handler, fields in (("Generate", {"Period": "01/03/2026"}),
                            ("Review", {"id": run_id}),
                            ("RequestApproval", {"id": run_id}),
                            ("Approve", {"id": run_id}),
                            ("Lock", {"id": run_id}),
                            ("Post", {"id": run_id}),
                            ("Reverse", {"id": posted, "reason": "audit probe"})):
        seam(dept, HRM_DB, "/hr/payroll", handler, fields, token_page="/hr/leave")

    # ------------------------------------------------------------------ AUD-AUZ-14
    case("AUD-AUZ-14", "HRM: leave — recommend and decide are separate grants on one page", "nasrin")
    check(dept.status("/hr/leave")[0] == 200, "nasrin (hr.leave.recommend) opens /hr/leave")
    seam(dept, HRM_DB, "/hr/leave", "Decide",
         {"id": leave_id, "approve": "true", "note": "audit probe"})
    seam(farid, HRM_DB, "/hr/leave", "Recommend", {"id": leave_id})

    # ------------------------------------------------------------------ AUD-AUZ-15
    case("AUD-AUZ-15", "HRM: the §12 payroll approver can reach the approval he is meant to give",
         "kamal")
    # §12 puts the lock approval with Accounts Manager / MD. The Accounts Manager holds
    # hr.payroll.approve and NOT hr.payroll.run, while the page that carries the Approve handler is
    # `[Authorize(Policy = HrPerm.PayrollRun)]`. If the page refuses him the approval is unreachable
    # by the only role §12 gives it to — a segregation-of-duties break in the safe direction, but a
    # break: whoever *can* reach the page must then hold both grants and approve their own run.
    # Spec 0039 WP3.6 split the surface: approvals live on /hr/payroll/approvals under
    # hr.payroll.approve, so the approver reaches the decision without the ability to
    # generate. The check asserts the INTENT — the §12 approver can reach an Approve
    # surface — and goes red again if that page is removed or its policy widens shut.
    kamal = H.Session("kamal")
    reachable = (kamal.status("/hr/payroll/approvals")[0] == 200
                 or kamal.status("/hr/payroll")[0] == 200)
    check(reachable,
          "kamal (Accounts Manager: hr.payroll.approve, hr.salary.read) can reach the "
          "payroll approval surface (/hr/payroll/approvals)")
    if not reachable:
        both = sql(HRM_DB, """
            SELECT string_agg(u.user_name, ',') FROM adm.app_user u
            JOIN adm.user_role ur ON ur.user_id = u.id
            WHERE EXISTS (SELECT 1 FROM adm.permission p WHERE p.role_id = ur.role_id
                          AND p.module='hr' AND p.action='payroll.run')
              AND EXISTS (SELECT 1 FROM adm.permission p WHERE p.role_id = ur.role_id
                          AND p.module='hr' AND p.action='payroll.approve')""")
        print(f"      .. only {both or '(nobody)'} holds both hr.payroll.run and "
              f"hr.payroll.approve, so only they can reach the Approve button")

    # ------------------------------------------------------------------ AUD-AUZ-16
    case("AUD-AUZ-16", "HR salary masking is view logic — hr.salary.read is no page's policy",
         "nasrin")
    # `hr.salary.read` never appears in an [Authorize] attribute: /hr/employees/{id} and
    # /hr/payroll are both `HrPerm.Read` / `PayrollRun`, so a department head is *on* the page and
    # the only thing between her and the numbers is `@if (Model.CanSeePay)`. shirin is the ERP
    # host's Department Head and nasrin the HRM host's — identical grants; the employee data lives
    # on the HRM host, so the figures are checked there.
    emp = sql(HRM_DB, """
        SELECT s.employee_id FROM hr.employee_pay_structure s
        JOIN hr.employee_pay_component c ON c.pay_structure_id = s.id
        ORDER BY c.amount_taka DESC LIMIT 1""")
    amounts = sql(HRM_DB, f"""
        SELECT string_agg(DISTINCT v::text, ',') FROM (
          (SELECT c.amount_taka AS v FROM hr.employee_pay_component c
             JOIN hr.employee_pay_structure s ON s.id = c.pay_structure_id
             WHERE s.employee_id = {emp or 0})
          UNION (SELECT gross_earnings_taka FROM hr.payroll_line ORDER BY 1 DESC LIMIT 3)
          UNION (SELECT net_pay_taka FROM hr.payroll_line ORDER BY 1 DESC LIMIT 3)
          UNION (SELECT total_net_taka FROM hr.payroll_run)) x""").split(",")
    # Only five-figure amounts: a 1,000 or 1,500 would collide with a leave balance or a bed
    # number somewhere on the page and turn a coincidence into a reported leak.
    figures = [a for a in amounts if a and a.isdigit() and int(a) >= 10000]
    print(f"      .. employee {emp}; real taka figures on these two screens: "
          f"{', '.join(figures) or '(none found)'}")

    def rendered(html: str, taka: str) -> str | None:
        """A taka figure as any of the three ways this UI can print it."""
        n = int(taka)
        forms = [taka, f"{n:,}"]                       # 137500, 137,500
        s, out = str(n), ""                            # 1,37,500 — Ui.Group's lakh grouping
        out, s = s[-3:], s[:-3]
        while s:
            out, s = s[-2:] + "," + out, s[:-2]
        forms.append(out)
        return next((f for f in forms if f in html), None)

    # Positive control first: the same figures on the same URL for a holder of hr.salary.read.
    # Without it "no figure found" would pass just as happily against an empty page.
    seen = [f for f in figures if rendered(farid.get(f"/hr/employees/{emp}"), f)]
    check(seen, f"control — farid (hr.salary.read) DOES see {seen or 'nothing'} on "
                f"/hr/employees/{emp}, so the grep can find a figure when one is printed")

    for path in (f"/hr/employees/{emp}", "/hr/payroll"):
        code, _ = dept.status(path)
        html = dept.get(path)
        if code != 200:
            check(True, f"{path} is refused outright for nasrin (page policy) — no figures to leak")
            continue
        leaked = [(f, rendered(html, f)) for f in figures if rendered(html, f)]
        check(not leaked,
              f"{path} renders no salary figure for nasrin (checked {len(figures)} real amounts"
              + (f"; LEAKED {leaked}" if leaked else "") + ")")
        check("৳" not in html,
              f"{path} prints no taka symbol at all for a viewer without hr.salary.read")

    # /hr/payroll's CanSeeMoney branch needs a role with hr.payroll.run and NOT hr.salary.read.
    # If the seed has nobody in that shape the branch is unreachable and therefore untested.
    naked = sql(HRM_DB, """
        SELECT coalesce(string_agg(DISTINCT r.name, ','), '(nobody)') FROM adm.role r
        WHERE EXISTS (SELECT 1 FROM adm.permission p WHERE p.role_id=r.id
                      AND p.module='hr' AND p.action='payroll.run')
          AND NOT EXISTS (SELECT 1 FROM adm.permission p WHERE p.role_id=r.id
                          AND p.module='hr' AND p.action='salary.read')""")
    print(f"      .. roles that can open /hr/payroll without hr.salary.read: {naked}")

    return report("Authorization seam probe — handler-guarded-in-body (spec 0038)")


if __name__ == "__main__":
    raise SystemExit(main())
