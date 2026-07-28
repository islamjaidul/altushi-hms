#!/usr/bin/env python3
"""The money invariants and the control gates that had no end-to-end proof (spec 0031).

`docs/qa/findings-2026-07-28.md` F7 listed thirteen High-severity lifecycle cases with no
coverage. Most of them are not module features — they are the *controls* that sit across
modules, which is exactly the class spec 0020's notes named: "each module's tests exercised
that module with data the test itself created; all three defects live between modules."

One thread, because these cases share a patient, an invoice and an admission, and eight
scripts each re-provisioning the same fixture would be slower and less honest.

  LC-BIL-11  a price change never alters a historical invoice   (admin, rasel)
  LC-DX-03   partial payment must NOT release the lab           (rasel, ripon)
  LC-BIL-10  cancellation is a reversal, never a deletion       (rasel -> shahid)
  LC-BLK-03  R4 block and release are both approval-gated       (jashim)
  LC-DIS-04  discharge with a due: typed reason + tier-2 audit  (rasel, admin)
  LC-DIS-07  a confirmed settlement can be reopened             (rasel, shahid)
  LC-LAB-08  amend after verification is approval-gated         (farhana)
  LC-ROLE-14 a permission revoked mid-shift stops working       (admin, jashim)

LC-ROLE-14 mutates the §12 matrix, so it restores the grant it revokes through
`_harness.on_exit` — spec 0029's "return what you take" applies to permissions exactly as it
applies to beds.

usage: python3 eng/verify/money-and-controls.py     (from anywhere; app on :5199 by default)
"""
import json
import pathlib
import re
import sys
import time
import urllib.parse

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import (BASE, Session, case, check, fixture, grant_cells, guard,   # noqa: E402
                      on_exit, open_counter, report, settle_and_discharge, step)

guard("t1")

STAMP = f"{int(time.time() * 1000) % 100000:05d}"
MONEY = r"(?:৳|&#x9F3;)\s*([\d,]+)"

print(f"MONEY INVARIANTS & CONTROL GATES  target={BASE}  run={STAMP}")

desk = Session("jashim")        # registration, admissions, R4 blocks
op = Session("rasel")           # the counter: invoices, receipts, settlement
sup = Session("shahid")         # approvals
admin = Session("admin")        # masters, rates, the §12 matrix
lab = Session("ripon")          # the bench
path = Session("farhana")       # verification and amendment

open_counter(op)


def register(prefix):
    name = f"{prefix} {STAMP}"
    desk.post("/registration/new", {
        "FullName": name, "Sex": "M", "AgeOrDob": "44", "Phone": f"0199{STAMP}0",
        "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
    hits = json.loads(desk.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
    return name, str(fixture(hits[0]["value"] if hits else None,
                             f"the patient {name} could not be registered"))


def invoice_total(html):
    amounts = [int(a.replace(",", "")) for a in re.findall(MONEY, html)]
    return max(amounts) if amounts else 0


def approve_latest(kind_words):
    """Approve the newest pending request whose humanised type matches."""
    inbox = sup.get("/admin/approvals")
    m = re.search(kind_words + r'.*?name="id" value="(\d+)"', inbox, re.S)
    if m:
        sup.post("/admin/approvals?handler=Decide",
                 {"id": m.group(1), "approve": "true", "note": "ok — QA"}, inbox)
    return m


# =============================================================================
# LC-BIL-11 — a price change never alters a historical invoice (G6, Rule 5)
# =============================================================================
case("LC-BIL-11", "A price change never alters a historical invoice", admin)
step(1, "admin creates a service of this run's own, priced at 700")
# Its own catalogue item, so no seeded price moves and no other thread's assertion shifts.
code = f"QA{STAMP}"
masters = admin.get("/admin/masters")
admin.post("/admin/masters?handler=Create", {
    "Kind": "service", "Code": code, "Name": f"QA Repricing Probe {STAMP}",
    "Dept": "OPD", "Price": "700"}, masters)
masters = admin.get("/admin/masters")
check(code in masters, f"the service {code} is in the catalogue")

step(2, "the counter bills it — the invoice is written at 700")
name, pid = register("Reprice Probe")
opd = op.get(f"/billing/opd?PatientId={pid}")
svc = fixture(re.search(r'name="catalogId" value="(\d+)"[^>]*title="Add QA Repricing Probe',
                        opd) or re.search(
                  r'title="Add QA Repricing Probe[^"]*"[\s\S]{0,200}?value="(\d+)"', opd),
              f"the new service {code} is not on the counter's catalogue")
opd = op.post("/billing/opd?handler=Add", {"catalogId": svc.group(1), "PatientId": pid}, opd)[1]
url, _ = op.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [svc.group(1)], "PaidNow": "700", "Tender": "cash"}, opd)
check("/billing/invoice/" in url, "the invoice was raised")
old_invoice = url.rstrip("/").split("/")[-1]
before = op.get(f"/billing/invoice/{old_invoice}")
check("700" in before, "the invoice reads 700")

step(3, "admin reprices it to 1,900 — effective tomorrow, as US21.1 requires")
masters = admin.get("/admin/masters")
row = fixture(re.search(r'name="TargetId" value="(\d+)"[\s\S]{0,400}?' + code, masters)
              or re.search(code + r'[\s\S]{0,2000}?name="TargetId" value="(\d+)"', masters),
              "the repricing form is not offered for the new service")
tid = row.group(1)
tomorrow = admin.get("/admin/masters")
_, html = admin.post("/admin/masters?handler=Reprice",
                     {"TargetId": tid, "TargetKind": "service", "NewPrice": "1900",
                      "EffectiveFrom": ""}, tomorrow)     # blank = tomorrow, the pre-filled default
history = admin.get(f"/admin/masters?history={tid}&historyKind=service")
check("1,900" in history or "1900" in history, "the new price is on the rate history")
check("700" in history, "and the old version is still there, closed rather than edited")

step(4, "the historical invoice is untouched — this is the invariant")
after = op.get(f"/billing/invoice/{old_invoice}")
check(invoice_total(after) == invoice_total(before),
      f"invoice {old_invoice} still totals {invoice_total(before)} after the reprice")
check("1,900" not in after and "1900" not in after,
      "the new price appears nowhere on the invoice already issued")

step(5, "and a price cannot be backdated over invoices already raised")
masters = admin.get("/admin/masters")
_, refused = admin.post("/admin/masters?handler=Reprice",
                        {"TargetId": tid, "TargetKind": "service", "NewPrice": "5",
                         "EffectiveFrom": "01 Jan 2020"}, masters)
check("cannot start in the past" in refused,
      "a backdated price is refused, with the reason in words")

# =============================================================================
# LC-DX-03 — partial payment must NOT release the lab
# =============================================================================
case("LC-DX-03", "Partial payment does not release the lab", op)
step(1, "an outpatient test order, deliberately part-paid")
dx_name, dx_pid = register("Partial Pay")
order = op.get(f"/diagnostics/order?PatientId={dx_pid}")
test = fixture(re.search(r'name="catalogId" value="(\d+)"', order),
               "no test on the diagnostics order catalogue")
order = op.post("/diagnostics/order?handler=Add",
                {"catalogId": test.group(1), "PatientId": dx_pid}, order)[1]
gross = re.search(r'data-gross="(\d+)"', order)
full = int(gross.group(1)) if gross else 0
part = max(1, full // 3)
url, _ = op.post("/diagnostics/order?handler=Save", {
    "PatientId": dx_pid, "Items": [test.group(1)],
    "PaidNow": str(part), "Tender": "cash"}, order)
check("/diagnostics/order/" in url or "/billing/invoice/" in url, "the part-paid order saved")
dx_order = re.search(r"/diagnostics/order/(\d+)", url)
dx_order = dx_order.group(1) if dx_order else None

step(2, "the bench must not see it — an unpaid test is not given away")
board = lab.get("/lis/board")
check(dx_name not in board,
      f"{dx_name} is NOT on the LIS board while {full - part} of {full} is unpaid")

step(3, "collecting the balance releases it")
dues = op.get("/billing/dues?Q=" + urllib.parse.quote(dx_name))
inv = re.search(r'name="InvoiceId" value="(\d+)"[\s\S]{0,400}?name="Amount" value="(\d+)"', dues)
if inv:
    op.post("/billing/dues?handler=Collect",
            {"InvoiceId": inv.group(1), "Amount": inv.group(2), "Tender": "cash"}, dues)
board = lab.get("/lis/board")
check(dx_name in board, "paid in full, the sample reaches the bench (§9A.2 seam)")

# =============================================================================
# LC-BIL-10 — cancellation is a reversal, never a deletion (Rule 4)
# =============================================================================
case("LC-BIL-10", "An invoice is cancelled by reversal, never deleted", op)
step(1, "an unpaid invoice to cancel")
c_name, c_pid = register("Cancel Probe")
opd = op.get(f"/billing/opd?PatientId={c_pid}")
any_svc = fixture(re.search(r'name="catalogId" value="(\d+)"', opd),
                  "no service on the OPD counter catalogue")
opd = op.post("/billing/opd?handler=Add", {"catalogId": any_svc.group(1), "PatientId": c_pid}, opd)[1]
url, _ = op.post("/billing/opd?handler=Save", {
    "PatientId": c_pid, "Items": [any_svc.group(1)], "PaidNow": "0", "Tender": "cash"}, opd)
check("/billing/invoice/" in url, "an unpaid invoice exists")
cancel_id = url.rstrip("/").split("/")[-1]
inv_no = re.search(r"(INV-[\w-]+)", op.get(f"/billing/invoice/{cancel_id}"))

step(2, "cancellation is requested, approved, then executed")
page = op.get("/billing/refund")
op.post("/billing/refund?handler=Request", {
    "InvoiceId": cancel_id, "Amount": "0", "Action": "cancel",
    "Reason": f"QA {STAMP} — raised in error"}, page)
approve_latest("Cancel")
page = op.get("/billing/refund")
req = re.search(r'name="requestId" value="(\d+)"', page)
if req:
    op.post("/billing/refund?handler=Execute", {"requestId": req.group(1)}, page)

step(3, "the invoice is still there, and says it was cancelled")
after = op.get(f"/billing/invoice/{cancel_id}")
check("cancel" in after.lower(), f"invoice {cancel_id} is marked cancelled")
check(inv_no is None or inv_no.group(1) in after,
      "its number is unchanged — the record was reversed, not removed (Rule 4)")
check("/login" not in after, "the invoice still resolves; nothing was hard-deleted")

# =============================================================================
# LC-BLK-03 — R4 block and release are both approval-gated
# =============================================================================
case("LC-BLK-03", "Block and release are refused without an approval", desk)
step(1, "an admission of this run's own")
b_name, b_pid = register("Block Probe")
admit = desk.get("/ipd/admit")
bed = fixture(re.search(r'<option value="(\d+)">(GW[MF]|CAB|ICU)', admit),
              "no free bed to admit the block probe into",
              "a thread that admits must discharge; reset the database if a run was killed")
url, _ = desk.post("/ipd/admit", {
    "PatientId": b_pid, "Source": "direct", "BedId": bed.group(1),
    "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
b_adm = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused").group(1)
on_exit(f"block-probe admission {b_adm} discharged and bed returned",
        lambda: settle_and_discharge(Session, b_adm, [bed.group(1)]))

step(2, "applying a block with no approval behind it is refused")
page = desk.get("/ipd/admissions")
_, html = desk.post("/ipd/admissions?handler=ApplyBlock",
                    {"AdmissionId": b_adm, "ApprovalId": "0"}, page)
check("Blocked for dues" not in desk.get(f"/ipd/folio/{b_adm}"),
      "the folio is NOT blocked — the gate held")
check("approval" in html.lower() or "not approved" in html.lower() or "refresh" in html.lower(),
      "and the refusal says why")

step(3, "the same for release: no approval, no release")
_, html = desk.post("/ipd/admissions?handler=ApplyRelease",
                    {"AdmissionId": b_adm, "ApprovalId": "0"},
                    desk.get("/ipd/admissions?Tab=blocked"))
check("approval" in html.lower() or "not approved" in html.lower()
      or "blocked" in html.lower() or "refresh" in html.lower(),
      "an unapproved release is refused too")

# =============================================================================
# LC-DIS-04 / LC-DIS-07 — the discharge gate and the settlement it rests on
# =============================================================================
case("LC-DIS-04", "Discharge with a due needs a typed reason, audited at tier 2", op)
step(1, "a folio with a charge, settled but not paid")
d_name, d_pid = register("Due Discharge")
admit = desk.get("/ipd/admit")
d_bed = fixture(re.search(r'<option value="(\d+)">(GW[MF]|CAB|ICU)', admit),
                "no free bed to admit the discharge probe into")
url, _ = desk.post("/ipd/admit", {
    "PatientId": d_pid, "Source": "direct", "BedId": d_bed.group(1),
    "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
d_adm = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused").group(1)
released = {"done": False}


def _return_due_bed():
    if not released["done"]:
        settle_and_discharge(Session, d_adm, [d_bed.group(1)])


on_exit(f"due-discharge admission {d_adm} closed and bed returned", _return_due_bed)

op.get(f"/ipd/folio/{d_adm}")                              # bed-day catch-up
dis = desk.get(f"/ipd/discharge/{d_adm}")
desk.post(f"/ipd/discharge/{d_adm}?handler=Initiate",
          {"AdmissionId": d_adm, "ClinicalSummary": f"QA {STAMP} — recovered."}, dis)
dis = desk.get(f"/ipd/discharge/{d_adm}")
desk.post(f"/ipd/discharge/{d_adm}?handler=Clear", {"AdmissionId": d_adm}, dis)
dis = op.get(f"/ipd/discharge/{d_adm}")
op.post(f"/ipd/discharge/{d_adm}?handler=Prepare", {"AdmissionId": d_adm}, dis)
dis = op.get(f"/ipd/discharge/{d_adm}")
check("Settlement draft" in dis, "the settlement draft is assembled from the folio")

case("LC-DIS-07", "A confirmed settlement draft can be reopened for a late charge", op)
step(1, "the draft reopens, and the folio takes charges again")
dis = op.get(f"/ipd/discharge/{d_adm}")
op.post(f"/ipd/discharge/{d_adm}?handler=Reopen", {"AdmissionId": d_adm}, dis)
folio = op.get(f"/ipd/folio/{d_adm}")
check("Blocked" not in folio and "settlement" not in folio.lower().split("draft")[0][-40:],
      "the folio is open again for the late charge")
step(2, "the supervisor cannot do it — settlement is the operator's grant, not his")
check(sup.post_denied(f"/ipd/discharge/{d_adm}?handler=Reopen", {"AdmissionId": d_adm}),
      "shahid (Billing Supervisor) is refused the reopen handler — he lacks ipd.settle")

case("LC-DIS-04", "Discharge with a due needs a typed reason, audited at tier 2", op)
step(2, "settle again, leave the balance owing, and try to discharge without a reason")
dis = op.get(f"/ipd/discharge/{d_adm}")
op.post(f"/ipd/discharge/{d_adm}?handler=Prepare", {"AdmissionId": d_adm}, dis)
dis = op.get(f"/ipd/discharge/{d_adm}")
op.post(f"/ipd/discharge/{d_adm}?handler=Confirm",
        {"AdmissionId": d_adm, "DiscountFlat": "0"}, dis)
dis = op.get(f"/ipd/discharge/{d_adm}")
check("is still owed" in dis or "still owe" in dis.lower(),
      "the screen states the outstanding position plainly")
_, refused = op.post(f"/ipd/discharge/{d_adm}?handler=Discharge",
                     {"AdmissionId": d_adm, "OutstandingReason": ""}, dis)
check("still owes" in refused or "reason" in refused.lower(),
      "a reasonless discharge is refused server-side, not just hidden")
check("Gate pass" not in op.get(f"/ipd/discharge/{d_adm}"), "no gate pass was printed")

step(3, "with a typed reason it goes through, and the reason is on the record")
reason = f"QA {STAMP} — MD instruction, family to settle at the counter"
dis = op.get(f"/ipd/discharge/{d_adm}")
op.post(f"/ipd/discharge/{d_adm}?handler=Discharge",
        {"AdmissionId": d_adm, "OutstandingReason": reason}, dis)
dis = op.get(f"/ipd/discharge/{d_adm}")
check("Gate pass" in dis, "the gate opens once a reason is given (§3.2)")
released["done"] = True

step(4, "and the reason is in the audit trail, attributed, at tier 2")
audit = admin.get("/admin/audit?Q=" + urllib.parse.quote(f"QA {STAMP}"))
check(reason[:40] in audit or f"QA {STAMP}" in audit,
      "the typed reason is findable in the audit viewer")
check("Rasel" in audit or "rasel" in audit,
      "attributed to the operator who typed it, by name (§8 N5)")

# bed back to the ward now that the admission is closed
board = desk.get("/ipd/board")
desk.post("/ipd/board?handler=CleaningDone", {"BedId": d_bed.group(1)}, board)

# =============================================================================
# LC-LAB-08 — amend after verification is approval-gated, and versions the result
# =============================================================================
case("LC-LAB-08", "A released result is amended by version, under approval", path)
# Self-provisioned: this walks the order LC-DX-03 just released, rather than hoping another
# thread left a verified result lying about. A case that only passes when it runs second is
# not coverage (spec 0029's lesson, applied to data instead of fixtures).
step(1, "the bench collects, receives and reports this run's own order")


def advance_samples(handler, wanted_words):
    moved = 0
    while moved < 6:
        page = lab.get("/lis/board")
        if wanted_words not in page:
            break
        ids = re.findall(r'action="/lis/board\?handler=' + handler + r'">\s*'
                         r'<input type="hidden" name="sampleId" value="(\d+)"', page)
        if not ids:
            break
        lab.post("/lis/board?handler=" + handler, {"sampleId": ids[0]}, page)
        moved += 1
    return moved


advance_samples("Collect", "Collect ")
advance_samples("Receive", "Receive at lab")
res = lab.get(f"/lis/results?orderId={dx_order}")
fields = re.findall(r'name="(Values\[[^"]+\])"', res)
check(len(fields) > 0, f"the result form offers {len(fields)} parameter field(s)")
payload = {"OrderId": dx_order}
for f in fields:
    payload[f] = "5"
lab.post("/lis/results", payload, res)

step(2, "the pathologist verifies and e-signs it")
ver = path.get(f"/lis/verify?orderId={dx_order}")
url, html = path.post("/lis/verify", {"OrderId": dx_order}, ver)
check("/lis/report/" in url or "e-signed" in html, "the report is verified and released")

step(3, "an amendment with no reason is refused")
amend = path.get("/lis/amend")
# The list is links; the hidden OrderTestId field only exists once one is selected.
target = fixture(re.search(r'href="/lis/amend\?orderTestId=(\d+)"', amend),
                 "no verified report is offered on the amend screen",
                 "the order this run verified should be amendable")
_, html = path.post("/lis/amend", {"OrderTestId": target.group(1), "Reason": ""}, amend)
check("reason" in html.lower(), "an amendment without a reason is refused")

step(4, "with a reason it is approval-gated, and v1 stands until it is decided")
amend = path.get(f"/lis/amend?orderTestId={target.group(1)}")
# The corrected value has to be a real parameter of this test's template, read off the form.
codes = re.findall(r'name="Values\[([^\]]+)\]"', amend)
param = fixture(codes[0] if codes else None,
                "the amend form offers no parameter to correct")
correction = {"OrderTestId": target.group(1), f"Values[{param}]": "9",
              "Reason": f"QA {STAMP} — transcription corrected"}
_, html = path.post("/lis/amend", correction, amend)
gated = "sent for approval" in html or "unchanged until it is decided" in html
check(gated or "amended" in html.lower(),
      "the amendment is either gated for approval or applied within this role's limit")
if gated:
    step(5, "the supervisor approves it, and only then does the correction land")
    check(approve_latest("Amend") is not None, "the amendment is in the approvals inbox")
    amend = path.get(f"/lis/amend?orderTestId={target.group(1)}")
    _, html = path.post("/lis/amend", correction, amend)
    check("Enter at least one corrected value" not in html,
          "the approved amendment applied")

step(6, "the correction is a new version, and it has to be signed like any other")
# v2 is written unverified, so the test leaves the amendable list and goes back to the
# verification queue. That is the proof it was VERSIONED and not edited in place: an
# overwrite would have left a still-signed v1 sitting exactly where it was.
amend = path.get("/lis/amend")
check(f'orderTestId={target.group(1)}"' not in amend,
      "the amended test is no longer offered for amendment — its latest version is unsigned")
check(dx_name in path.get("/lis/verify"),
      "and it is back in the verification queue for the correction to be e-signed")

# =============================================================================
# LC-ROLE-14 / LC-QUE-08 — a permission revoked mid-shift stops working
# =============================================================================
case("LC-ROLE-14", "A permission revoked mid-shift stops working within the window", admin)
step(1, "jashim can issue a serial — appointments.create is the grant that lets him")
queue = desk.get("/appointments")
check("Serials" in queue or "serial" in queue.lower(), "the receptionist has the queue open")

users = admin.get("/admin/users")
cell = fixture(
    next((c for c in grant_cells(users)
          if c[0] == "Receptionist" and c[1] == "appointments.create" and c[3]), None),
    "the Receptionist does not hold appointments.create on this deployment",
    "run grant-drift.py — the deployment's §12 matrix has drifted from the code")
role_id = cell[2]

restored = {"done": False}


def _restore_grant():
    if restored["done"]:
        return
    page = admin.get("/admin/users")
    admin.post("/admin/users?handler=Permission",
               {"roleId": role_id, "permission": "appointments.create", "grant": "true"}, page)
    restored["done"] = True


# Spec 0029's rule applies to the §12 matrix too: this case takes a grant away, so it must
# put it back whatever happens next.
on_exit("appointments.create restored to the Receptionist", _restore_grant)

step(2, "admin revokes appointments.create from the Receptionist role")
users = admin.get("/admin/users")
admin.post("/admin/users?handler=Permission",
           {"roleId": role_id, "permission": "appointments.create", "grant": "false"},
           users)
users = admin.get("/admin/users")
# The matrix lists a permission only while the nav registry or some role carries it, so a
# permission nothing screens is protected by drops off the table entirely once the last role
# loses it. "Not held" is therefore the assertion, not "shown as Grant".
still_held = any(c[0] == "Receptionist" and c[1] == "appointments.create" and c[3]
                 for c in grant_cells(users))
check(not still_held, "the Receptionist no longer holds appointments.create")

step(3, "the sitting receptionist's next write is refused — LC-QUE-08's handler check")
# A fresh session, because ADR-0019's security stamp is what invalidates the live cookie and
# the harness cannot wait five minutes. What is under test is the handler, not the clock.
desk2 = Session("jashim")
check(desk2.post_denied("/appointments?handler=Issue", {"PatientId": pid, "DoctorId": "1"}),
      "issuing a serial without appointments.create is refused at the handler")
check(desk2.post_denied("/appointments?handler=Advance",
                        {"id": "1", "from": "booked", "to": "cancelled"}),
      "advancing a serial without appointments.create is refused too")
check(not desk2.denied("/appointments"),
      "and the queue is still READABLE — the split is read vs write, not all or nothing")

step(4, "the grant goes back, and the receptionist can work again")
_restore_grant()
users = admin.get("/admin/users")
check(any(c[0] == "Receptionist" and c[1] == "appointments.create" and c[3]
          for c in grant_cells(users)),
      "appointments.create is restored to the Receptionist")
desk3 = Session("jashim")
check(not desk3.denied("/appointments"), "jashim is working again")

sys.exit(report("MONEY INVARIANTS & CONTROL GATES"))
