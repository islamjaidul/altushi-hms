#!/usr/bin/env python3
"""LC-ROLE — a day in the life of each of the twelve roles, plus the authorization matrix.

This is the *positive* half of authorization, which nothing else in the repo asserts. The
Playwright suite proves nineteen hand-picked denied pairs; no test anywhere proves that a role
can actually do its own job, or that the twelve seeded roles collectively cover every screen.

For each role: log in, load the landing page, walk every protected route, and require the
outcome the grant matrix predicts — reachable where the role holds the permission, refused
where it does not. Twelve roles across sixty-four protected routes is 768 assertions, which is
why it is a table rather than prose.

Read-only (tier 0): it logs in and issues GETs, plus a handful of POSTs that are expected to be
refused and therefore write nothing. Safe against any environment, including production.

  python3 role-journeys.py
  BASE_URL=https://hms.example.com python3 role-journeys.py
"""
import sys

from _harness import CAST, Session, case, check, guard, report

guard("t0")

# ---------------------------------------------------------------------------
# Mirrors src/Hms.Web/DevSeed.cs `Roles`. check-lifecycle-traceability.sh asserts it matches.
# ---------------------------------------------------------------------------
ROLE_GRANTS = {
    "Receptionist": [
        "registration.create", "registration.read", "appointments.read",
        "appointments.create", "ipd.read", "ipd.manage"],
    "Billing Operator": [
        "registration.read", "billing.invoice.create", "billing.receipt.create",
        "billing.session.open", "billing.session.close", "diagnostics.order.create",
        "ipd.read", "ipd.settle"],
    "Lab Technologist": [
        "registration.read", "lis.worklist.read", "lis.sample.collect", "lis.result.enter"],
    "Pathologist": [
        "registration.read", "lis.worklist.read", "lis.result.enter", "lis.result.verify",
        "radiology.worklist.read", "radiology.report.write"],
    "Radiology Technician": [
        "registration.read", "radiology.worklist.read", "radiology.study.perform"],
    "Billing Supervisor": [
        "registration.read", "billing.invoice.create", "billing.receipt.create",
        "billing.session.close", "admin.approvals.decide", "ipd.read"],
    "Nurse": [
        "registration.read", "ipd.read", "ipd.service.post",
        "emr.read", "emr.vitals.record", "emr.chart.record"],
    "OPD Consultant": [
        "registration.read", "emr.read", "emr.note.write", "diagnostics.order.create",
        "lis.worklist.read", "ipd.read", "ot.read"],
    "OT In-charge": [
        "registration.read", "ipd.read", "ot.read", "ot.schedule", "ot.record",
        "pharmacy.read"],
    "Admin": [
        "admin.users.manage", "admin.audit.read", "admin.approvals.decide",
        "admin.masters.manage", "notifications.read"],
    "MD": [
        "dashboard.read", "admin.approvals.decide", "admin.audit.read", "pharmacy.read",
        "ipd.read"],
    "Pharmacist": [
        "registration.read", "pharmacy.read", "pharmacy.sale.create",
        "pharmacy.purchase.manage", "pharmacy.stock.manage", "billing.session.open",
        "billing.session.close", "billing.receipt.create"],
}

# ---------------------------------------------------------------------------
# Mirrors the [Authorize(Policy = Perm.X)] attribute on each page model, keyed by the route the
# page's `@page` directive actually declares — several override the folder path and several
# take a route parameter. Probing `/billing/dayclose` instead of `/billing/day-close` returns
# 404, which is indistinguishable from "allowed" unless you look, so the literal route matters.
# `eng/check-lifecycle-traceability.sh` re-extracts both halves from source and compares.
# ---------------------------------------------------------------------------
ROUTES = {
    "/admin/approvals": "admin.approvals.decide",
    "/admin/audit": "admin.audit.read",
    "/admin/import": "admin.masters.manage",
    "/admin/masters": "admin.masters.manage",
    "/admin/people": "admin.masters.manage",
    "/admin/sms": "admin.masters.manage",
    "/admin/templates": "admin.masters.manage",
    "/admin/users": "admin.users.manage",
    "/appointments": "appointments.read",
    "/billing/day-close": "billing.session.close",
    "/billing/dues": "billing.receipt.create",
    "/billing/invoice/1": "registration.read",
    "/billing/opd": "billing.invoice.create",
    "/billing/refund": "billing.receipt.create",
    "/billing/reports": "billing.session.close",
    "/billing/session": "billing.session.open",
    "/billing/statement/1": "billing.session.close",
    "/dashboard": "dashboard.read",
    "/diagnostics/delivery": "diagnostics.order.create",
    "/diagnostics/order": "diagnostics.order.create",
    "/diagnostics/order/1": "diagnostics.order.create",
    "/emr/charts": "emr.chart.record",
    "/emr/consult/1": "emr.note.write",
    "/emr/history": "emr.read",
    "/emr/prescription/1": "emr.read",
    "/emr/queue": "emr.read",
    # M16 HR & Payroll (spec 0036). These screens ship in a razor class library so the same build
    # serves the standalone HRM SKU (ADR-0025); the traceability guard now scans every Pages root,
    # so they must be listed here like any other module's.
    "/hr": "hr.read",
    "/hr/attendance": "hr.attendance.review",
    "/hr/employees": "hr.read",
    "/hr/employees/new": "hr.employee.manage",
    "/hr/leave": "hr.read",
    "/hr/me": "hr.leave.apply",
    "/hr/payroll": "hr.payroll.run",
    "/hr/policies": "hr.policy.manage",
    "/hr/roster": "hr.roster.manage",
    "/emr/templates": "emr.note.write",
    "/emr/vitals": "emr.vitals.record",
    "/frontdesk": "ipd.read",
    "/ipd/admissions": "ipd.read",
    "/ipd/admit": "ipd.manage",
    "/ipd/board": "ipd.read",
    "/ipd/certificates": "ipd.settle",
    "/ipd/discharge/1": "ipd.read",
    "/ipd/folio/1": "ipd.read",
    "/ipd/indents": "ipd.service.post",
    "/ipd/reports": "ipd.read",
    "/lis/amend": "lis.result.verify",
    "/lis/board": "lis.worklist.read",
    "/lis/report/1": "lis.worklist.read",
    "/lis/results": "lis.result.enter",
    "/lis/verify": "lis.result.verify",
    "/notifications/tray": "notifications.read",
    "/ot/board": "ot.read",
    "/ot/case/1": "ot.record",
    "/ot/register": "ot.read",
    "/ot/schedule": "ot.schedule",
    "/ot/theatres": "ot.schedule",
    "/pharmacy/dashboard": "pharmacy.read",
    "/pharmacy/indents": "pharmacy.stock.manage",
    "/pharmacy/pos": "pharmacy.sale.create",
    "/pharmacy/products": "pharmacy.purchase.manage",
    "/pharmacy/purchase": "pharmacy.purchase.manage",
    "/pharmacy/reports": "pharmacy.read",
    "/pharmacy/stock": "pharmacy.stock.manage",
    "/pharmacy/suppliers": "pharmacy.purchase.manage",
    "/pharmacy/transfers": "pharmacy.stock.manage",
    "/radiology/modalities": "radiology.study.perform",
    "/radiology/print/1": "radiology.worklist.read",
    "/radiology/report/1": "radiology.report.write",
    "/radiology/worklist": "radiology.worklist.read",
    "/registration": "registration.read",
    "/registration/1/card": "registration.read",
    "/registration/new": "registration.create",
}

# The one screen each role opens first — its core duty, asserted positively.
CORE_DUTY = {
    "jashim": ("/registration/new", "register a patient"),
    "rasel": ("/billing/opd", "bill an OPD visit"),
    "ripon": ("/lis/results", "enter a lab result"),
    "farhana": ("/lis/verify", "verify and e-sign a result"),
    "moinul": ("/radiology/modalities", "perform a study"),
    "shahid": ("/admin/approvals", "decide an approval"),
    "nasrin": ("/emr/charts", "record a nursing chart"),
    "chowdhury": ("/emr/consult/1", "write a prescription"),
    "shaheen": ("/ot/schedule", "schedule a theatre case"),
    "parvin": ("/pharmacy/pos", "sell medicine"),
    "admin": ("/admin/users", "manage users"),
    "md": ("/dashboard", "read the dashboard"),
}

# The adjacent duty each role must be refused — separation of duties in one line each.
ADJACENT = {
    "jashim": ("/billing/opd", "cannot bill"),
    "rasel": ("/emr/consult/1", "cannot write a prescription"),
    "ripon": ("/lis/verify", "cannot verify the result it entered (four eyes)"),
    "farhana": ("/billing/opd", "cannot bill"),
    "moinul": ("/radiology/report/1", "cannot write the report it performed"),
    "shahid": ("/ipd/certificates", "cannot settle, despite being the supervisor"),
    "nasrin": ("/emr/consult/1", "cannot write or sign a prescription"),
    "chowdhury": ("/billing/opd", "cannot bill for what it ordered"),
    "shaheen": ("/billing/opd", "cannot bill the case it recorded"),
    "parvin": ("/emr/consult/1", "cannot prescribe what it dispenses"),
    "admin": ("/billing/opd", "cannot bill"),
    "md": ("/billing/opd", "cannot bill"),
}

print(f"ROLE JOURNEYS — {len(CAST)} roles x {len(ROUTES)} protected routes")

sessions = {}
for user, role in CAST.items():
    n = list(CAST).index(user) + 1
    case(f"LC-ROLE-{n:02d}", f"{role} does its job and nothing else", user)
    try:
        s = Session(user)
    except Exception as e:                                   # noqa: BLE001
        check(False, f"{user} cannot log in: {e}")
        continue
    sessions[user] = s
    grants = set(ROLE_GRANTS[role])

    check(not s.denied("/"), f"{user} reaches the landing page")

    duty, what = CORE_DUTY[user]
    check(not s.denied(duty), f"CAN {what} ({duty})")

    adj, why = ADJACENT[user]
    check(s.denied(adj), f"CANNOT {why} ({adj})")

    wrong_open, wrong_shut = [], []
    for route, perm in sorted(ROUTES.items()):
        denied = s.denied(route)
        if perm in grants and denied:
            wrong_shut.append(route)
        elif perm not in grants and not denied:
            wrong_open.append(f"{route} (needs {perm})")
    check(not wrong_open,
          f"no route reachable without its permission" +
          (f" — LEAKED: {', '.join(wrong_open)}" if wrong_open else ""))
    check(not wrong_shut,
          f"every granted route is reachable" +
          (f" — BLOCKED: {', '.join(wrong_shut)}" if wrong_shut else ""))

# --- cross-cutting authorization ------------------------------------------
case("LC-XCUT-01", "The dashboard belongs to the MD alone", "md")
for user, s in sessions.items():
    if user == "md":
        check(not s.denied("/dashboard"), "md reads the dashboard")
    else:
        check(s.denied("/dashboard"), f"{user} ({CAST[user]}) is refused the dashboard")

case("LC-XCUT-02", "Anonymous surfaces answer without leaking", "")
anon = Session.__new__(Session)
import urllib.request                                        # noqa: E402
import http.cookiejar                                        # noqa: E402
from _harness import BASE                                    # noqa: E402
anon.op = urllib.request.build_opener(
    urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
anon.user, anon.role = "anonymous", "-"
for path in ("/login", "/public/queue", "/public/report-status", "/health"):
    try:
        body = anon.op.open(BASE + path).read().decode()
        check(True, f"{path} answers anonymously")
        if path == "/public/queue":
            check("Demo#1234" not in body, "queue display leaks no credential")
    except Exception as e:                                   # noqa: BLE001
        check(False, f"{path} should answer anonymously: {e}")
for path in ("/registration", "/billing/opd", "/admin/users", "/ipd/folio/1"):
    try:
        anon.op.open(BASE + path)
        landed = anon.op.open(BASE + path).geturl()
        check("/login" in landed, f"{path} sends an anonymous visitor to login")
    except Exception:                                        # noqa: BLE001
        check(True, f"{path} refuses an anonymous visitor")

case("LC-XCUT-03", "Hiding a button is not the control — handlers refuse too", "nasrin")
if "nasrin" in sessions:
    check(sessions["nasrin"].post_denied(
        "/emr/consult/1?handler=Finalise", {"NoteId": "1"}),
        "nurse POST to the prescription finalise handler is refused")
if "ripon" in sessions:
    check(sessions["ripon"].post_denied(
        "/lis/verify?handler=Verify", {"ResultId": "1"}),
        "technologist POST to the verify handler is refused")

sys.exit(report("ROLE JOURNEYS", require_all_roles=True))
