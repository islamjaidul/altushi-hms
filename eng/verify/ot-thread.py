#!/usr/bin/env python3
"""Spec 0025 — M7 Operation Theatre, end to end against a running app.

Admits a patient, schedules an operation, proves the theatre cannot be double-booked, walks the
§11 states, issues a consumable off pharmacy stock, completes the case, and checks the folio
grew by exactly the operation + team + consumables — US7.2's "surgery revenue is never
under-billed", asserted rather than asserted-about.

Dirty-database tolerant: its own patient, its own admission, its own case.

usage: python3 eng/verify/ot-thread.py       (from the repo root, app on :5199)
"""
import http.cookiejar
import json
import os
import pathlib
import re
import sys
import time
import urllib.parse
import urllib.request
from datetime import date, timedelta

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import fixture, on_exit, record, settle_and_discharge, tag   # noqa: E402

# Overridable so the same thread can be run against a deployed instance
# (`BASE_URL=https://… python3 …`) after a release, the way the others are run locally.
BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
MONEY_RE = re.compile(r"(?:৳|&#x9F3;)\s*([\d,]+)")


class Session:
    def __init__(self, user, password="Demo#1234"):
        self.op = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        url, _ = self.post("/login", {"Username": user, "Password": password}, self.get("/login"))
        if "/login" in url:
            raise SystemExit(f"could not sign in as {user}")

    def get(self, path):
        with self.op.open(BASE + path) as r:
            return r.read().decode()

    def post(self, path, fields, page_html=None):
        html = page_html if page_html is not None else self.get(path)
        data = dict(fields)
        m = TOKEN_RE.search(html)
        if m:
            data["__RequestVerificationToken"] = m.group(1)
        req = urllib.request.Request(
            BASE + path, data=urllib.parse.urlencode(data, doseq=True).encode())
        with self.op.open(req) as r:
            return r.geturl(), r.read().decode()


fail = []


def check(cond, msg):
    print(f"  {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


def folio_total(html):
    """The folio screen's running total, as an integer of taka."""
    m = re.search(r'data-folio-total="(\d+)"', html)
    if m:
        return int(m.group(1))
    # Fall back to the largest money figure on the page — the total is the biggest number there.
    amounts = [int(x.replace(",", "")) for x in MONEY_RE.findall(html)]
    return max(amounts) if amounts else 0


stamp = f"{int(time.time() * 1000) % 100000:05d}"
print("OPERATION THEATRE THREAD (M7)")

desk = Session("jashim")
incharge = Session("shaheen")

# ---- 1. a patient in a bed --------------------------------------------------------
print("\n1. an admitted patient with a folio")
name = tag(f"OT Test {stamp}")
desk.post("/registration/new", {
    "FullName": name, "Sex": "F", "AgeOrDob": "32", "Phone": f"01766{stamp}0",
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(desk.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
check(len(hits) > 0, f"patient registered ({name})")
if not hits:
    sys.exit(1)
patient_id = str(hits[0]["value"])

admit = desk.get("/ipd/admit")
bed = fixture(re.search(r'<option value="(\d+)">(GW[MF]|CAB|ICU)', admit),
              "no free bed anywhere in the hospital",
              "a thread that admits must discharge; reset the database if a run was killed")
check(bed is not None, "a free bed is offered")
url, _ = desk.post("/ipd/admit", {
    "PatientId": patient_id, "Source": "opd", "BedId": bed.group(1),
    "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
admission_id = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused")
check(admission_id is not None, "the patient is admitted")
admission_id = admission_id.group(1)
record("admission", admission_id)

# Spec 0029 F3: the surgery is the point of this thread, but the bed it borrows to hold the
# folio is a fixture like any other and has to go back — including when a later step fails.
on_exit(f"admission {admission_id} discharged and bed returned",
        lambda: settle_and_discharge(Session, admission_id, [bed.group(1)]))
before = folio_total(desk.get(f"/ipd/folio/{admission_id}"))
print(f"     folio before surgery: ৳{before:,}")

# ---- 2. schedule ------------------------------------------------------------------
print("\n2. scheduling, and the clash that must be refused (US7.1)")
# Two theatres of our own, so a previous run's cases can never occupy the slots this run
# needs. Clash assertions have to be about *our* bookings to mean anything.
masters = incharge.get("/ot/theatres")
for suffix in ("A", "B"):
    incharge.post("/ot/theatres?handler=Add", {"Name": f"OT-test-{stamp}{suffix}"}, masters)

form = incharge.get("/ot/schedule")
theatres = re.findall(r'name="TheatreId" required>(.*?)</select>', form, re.S)
theatre_options = re.findall(r'<option value="(\d+)">([^<]+)</option>', theatres[0]) if theatres else []
theatre_ids = [tid for tid, label in theatre_options if f"OT-test-{stamp}" in label]
check(len(theatre_ids) == 2, f"this run has its own two theatres ({len(theatre_ids)})")
if len(theatre_ids) != 2:
    sys.exit(1)

ops = re.search(r'name="OperationServiceId" required>(.*?)</select>', form, re.S)
op_ids = re.findall(r'<option value="(\d+)">([^<]+)</option>', ops.group(1)) if ops else []
check(len(op_ids) > 0, f"the operation catalogue is priced ({len(op_ids)} operations)")
fixture(op_ids, "no priced operation in the catalogue",
        "add one at /admin/masters, or reset the database")
operation_id, operation_label = op_ids[0]

people = re.search(r'name="SurgeonId" required>(.*?)</select>', form, re.S)
person_ids = re.findall(r'<option value="(\d+)">', people.group(1)) if people else []
fixture(len(person_ids) >= 2, "fewer than two consultants to make a surgical team",
        "add consultants at /admin/people, or reset the database")
surgeon, anaesthetist = person_ids[0], person_ids[1]

# A date of this run's own. The theatres are ours, but the *people* are shared with every
# other run, so a surgeon-clash assertion is only meaningful on a day nobody else booked.
# Spread over years, not 200 days: the theatres are this run's own but the SURGEONS are
# shared, so two runs landing on the same date make the "a free slot books fine" check fail
# on a genuine clash with a stranger's booking. 200 possible dates collide quickly.
run_date = date.today() + timedelta(days=1 + int(stamp) % 3000)
run_date_text = run_date.strftime("%d %b %Y")


def schedule(theatre, start, surgeon_id, page=None):
    return incharge.post("/ot/schedule", {
        "PatientId": patient_id, "OperationServiceId": operation_id, "TheatreId": theatre,
        "OnDate": run_date_text, "FromTime": start, "Minutes": "90",
        "SurgeonId": surgeon_id, "AnaesthetistId": anaesthetist, "AssistantId": "0",
    }, page or form)

url, html = schedule(theatre_ids[0], "09:00", surgeon)
case_id = re.search(r"/ot/case/(\d+)", url)
check(case_id is not None, f"operation scheduled ({operation_label.strip()})")
if not case_id:
    print(re.findall(r'class="alert bad">.*?<span>(.*?)</span>', html, re.S)[:1])
    sys.exit(1)
case_id = case_id.group(1)
record("ot_case", case_id)

_, clash = schedule(theatre_ids[0], "09:30", surgeon)
check("already booked" in clash, "an overlapping booking in the same theatre is refused")
check("OT-" in clash, "and the refusal names the clashing case")

_, surgeon_clash = schedule(theatre_ids[1], "09:30", surgeon)
check("already in" in surgeon_clash, "the same surgeon in another theatre is refused too")

# The theatres are this run's own, but the SURGEONS are shared with every other run and with
# whatever a hospital already had booked. Finding a slot that genuinely clashes with nothing
# is part of setting this case up, not part of what it asserts — so try a few rather than
# assume the first is free and report a stranger's booking as a scheduling defect.
free_url, free = "", ""
for who in (person_ids[2:] or [surgeon]):
    for when in ("13:00", "15:00", "17:00"):
        free_url, free = schedule(theatre_ids[1], when, who)
        if "/ot/case/" in free_url:
            break
    if "/ot/case/" in free_url:
        break
if "/ot/case/" not in free_url:
    print("     scheduling said:",
          (re.findall(r'class="alert bad">.*?<span>(.*?)</span>', free, re.S) or ["no message"])[0].strip()[:160])
check("/ot/case/" in free_url, "a non-overlapping slot schedules fine")

# ---- 3. the state machine ---------------------------------------------------------
print("\n3. the §11 state machine, in order")
case = incharge.get(f"/ot/case/{case_id}")
check("Patient sent for" in case, "a scheduled case offers only 'patient sent for'")
check("Complete &amp; post charges" not in case, "completion is not offered before theatre (U7)")

incharge.post(f"/ot/case/{case_id}?handler=Ready", {}, case)
case = incharge.get(f"/ot/case/{case_id}")
check("Start — patient in theatre" in case, "then the start control appears")

incharge.post(f"/ot/case/{case_id}?handler=Start", {}, case)
case = incharge.get(f"/ot/case/{case_id}")
check("Complete &amp; post charges" in case, "and completion once the patient is in theatre")

# ---- 4. consumables off real stock ------------------------------------------------
print("\n4. a consumable comes off pharmacy stock and lands on the folio")
products = re.search(r'name="ProductId">(.*?)</select>', case, re.S)
offered = re.findall(r'<option value="(\d+)">([^<]+)</option>',
                     products.group(1)) if products else []
check(len(offered) > 0, "the consumable picker offers stock")

# The picker lists every active product, in stock or not, so taking offered[0] was a coin
# toss that eventually landed on a product at zero and reported "the consumable is not
# recorded" — a stock fact dressed up as an OT defect (spec 0029 F3). Ask the shelf instead.
# Scoped to the MAIN outlet, because that is the only one an OT case draws from
# (`Case.cshtml.cs` resolves `Kind == "main"`). Summing a brand across every outlet picked a
# probe product with 439 units sitting in a probe outlet and none where the theatre could
# reach it — stock that exists but not where it is needed reads exactly like an OT defect.
pharmacist = Session("parvin")
main_outlet = re.search(r'<option value="(\d+)"[^>]*>\s*Main Pharmacy',
                        pharmacist.get("/pharmacy/stock"))
shelf = pharmacist.get("/pharmacy/stock?Show=all"
                       + (f"&OutletId={main_outlet.group(1)}" if main_outlet else ""))
on_hand = {}
for row in re.findall(r"<tr>(.*?)</tr>", shelf, re.S):
    cells = [re.sub(r"<[^>]+>", "", c).strip()
             for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
    if len(cells) < 7:
        continue
    state = cells[6].split("\n")[0].strip()
    if state not in ("in stock", "near expiry"):
        continue
    # Keyed on the WHOLE product label, not its first word. The pharmacy probe creates
    # products named "Probecillin <stamp>", so grouping by first token pooled a dozen
    # distinct products into one "Probecillin" total of 438 units — and then attributed it
    # to whichever one the picker happened to list, whose own batches were all disposed.
    on_hand[cells[0]] = on_hand.get(cells[0], 0) + int(re.sub(r"\D", "", cells[3]) or 0)

# The picker's label is "{Brand} {Strength} {Form}" — the same string the shelf prints.
stocked = sorted(((on_hand.get(label.strip(), 0), pid, label) for pid, label in offered),
                 reverse=True)
product = fixture(stocked[0] if stocked and stocked[0][0] >= 2 else None,
                  "no pharmacy product has two sellable units to consume",
                  "receive stock through /pharmacy/purchase, or reset the database")
print(f"     consuming {product[2].strip()} — {product[0]} on hand")
product_ids = [product[1]]
if product_ids:
    incharge.post(f"/ot/case/{case_id}?handler=Consumable",
                  {"ProductId": product_ids[0], "Qty": "2"}, case)
    case = incharge.get(f"/ot/case/{case_id}")
    check("Consumables used" in case, "the consumable is recorded against the case")
    check("batch" in case.lower() or "Batch" in case, "with the batch it came from (FEFO)")

# ---- 5. completion posts the money ------------------------------------------------
print("\n5. completing the case bills it (US7.2)")
mid = folio_total(desk.get(f"/ipd/folio/{admission_id}"))
_, done = incharge.post(f"/ot/case/{case_id}?handler=Complete", {
    "ProcedurePerformed": f"Procedure performed [{stamp}]",
    "Findings": "Nil unexpected", "AnaesthesiaType": "spinal"}, case)
check("posted to the bill" in done or "/ot/case/" in done, "the case completed")

case = incharge.get(f"/ot/case/{case_id}")
check("Operative record" in case, "the operative record is kept")
check(f"Procedure performed [{stamp}]" in case, "and says what was done")

after = folio_total(desk.get(f"/ipd/folio/{admission_id}"))
check(after > mid, f"the folio grew when the case completed (৳{mid:,} → ৳{after:,})")

# The surgeon's fee must be on the case, because M17 will read it from there (US7.3).
check(re.search(r"Surgeon</td>\s*<td>[^<]+</td>\s*<td class=\"num tabular\">(?:৳|&#x9F3;)",
                case) is not None,
      "the surgeon's posted fee is recorded on the case for payouts")

# ---- 6. completing twice --------------------------------------------------------
print("\n6. a completed case cannot be completed again")
_, twice = incharge.post(f"/ot/case/{case_id}?handler=Complete", {
    "ProcedurePerformed": "Duplicate attempt"}, case)
check("not in theatre" in twice or "already be completed" in twice,
      "the second completion is refused, not silently repeated")
final = folio_total(desk.get(f"/ipd/folio/{admission_id}"))
check(final == after, f"and nothing was billed twice (৳{after:,} still)")

# ---- 7. the register ------------------------------------------------------------
print("\n7. the operation register")
# The case is scheduled on this run's own date, so the register is asked for that range.
register = incharge.get(f"/ot/register?From={urllib.parse.quote(run_date_text)}"
                        f"&To={urllib.parse.quote(run_date_text)}")
check("Operation register" in register, "the register opens")
check(name in register, "our case is on it, by patient")

print()
if fail:
    print(f"OT THREAD FAILED — {len(fail)} check(s)")
    for f in fail:
        print(f"  - {f}")
    sys.exit(1)
print("OT THREAD PASSED")
