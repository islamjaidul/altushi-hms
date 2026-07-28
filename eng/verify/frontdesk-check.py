#!/usr/bin/env python3
"""M2 Front Desk / Help Desk — the enquiry window's three panels (spec 0018 AC 1, spec 0032 M2).

US2.2's estimate equals posted charges + accrued bed days − advances, and reading it changes
nothing. That much this script always proved. What it did NOT prove, while the lifecycle document
credited it anyway, was LC-FD-05: the *Beds free now* panel was never read at all — the free bed
this script looks at is the dropdown on `/ipd/admit`, a different screen, used for fixture setup.
Playwright asserts the panel's heading, so an empty table under a correct heading passed.

Both side panels are now asserted against state this script itself changes:

  * LC-FD-05 — admitting into a class must decrement that class's *Free* by exactly one and leave
    *Total* alone; returning the bed must put it back. A delta the run causes is immune to
    whatever else is in the ward, and it cannot pass against an empty panel.
  * LC-FD-06 — issuing a serial must move the doctor's booked/waiting counts on the same screen,
    and completing it must move waiting into done. US2.1's first question is "when is the doctor
    available?" and nothing checked the numbers that answer it.

Dirty-database-tolerant (registers its own patient, frees the bed afterwards); money figures rely
only on seeded constant rates (bed 1200, admission fee 500).
"""
import json
import pathlib
import re
import sys
import time
import urllib.parse

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import (Session, case, check, fixture, guard, on_exit,   # noqa: E402
                      record, release_bed, report, tag)

MONEY = r'(?:&#x9F3; |৳ )([\d,]+)'


def estimate(html):
    """The 'Net payable now' figure, whatever the taka sign was encoded as."""
    m = re.search(r'Net payable now[^৳&]*' + MONEY, html)
    return None if m is None else int(m.group(1).replace(",", ""))


def beds_panel(html):
    """`{CLASS: (free, total)}` from the 'Beds free now' card — the panel LC-FD-05 is about.

    Sliced from the heading first: the page carries three tables and the doctors one also uses
    `td.tabular`. The `<b>` around Free is what makes the row shape unambiguous.
    """
    if "Beds free now" not in html:
        return {}
    section = html.split("Beds free now", 1)[1]
    return {cls.strip().upper(): (int(free), int(total)) for cls, free, total in re.findall(
        r'<td>([A-Za-z ]+)</td>\s*<td class="tabular"><b>(\d+)</b></td>\s*'
        r'<td class="tabular">(\d+)</td>', section)}


def doctors_panel(html):
    """`{name: (booked, waiting, done)}` from the "Today's doctors" card (US2.1)."""
    if "Today's doctors" not in html:
        return {}
    section = html.split("Today's doctors", 1)[1].split("Beds free now", 1)[0]
    return {name.strip(): (int(b), int(w), int(d)) for name, _room, b, w, d in re.findall(
        r'<td>([^<]+)</td>\s*<td>([^<]*)</td>\s*<td class="tabular">(\d+)</td>\s*'
        r'<td class="tabular">(\d+)</td>\s*<td class="tabular">(\d+)</td>', section)}


guard("t1")

print("FRONT DESK / HELP DESK (M2)")
fd = Session("jashim")          # Receptionist — ipd.read, appointments.create
op = Session("rasel")           # Billing Operator — takes the advance

stamp = f"{int(time.time() * 1000) % 100000:05d}"   # ms: two runs in one second differ
NAME = tag(f"Desk Check {stamp}")

# --- fixture: one patient of this run's own -----------------------------------
# Unique phone per run, and acknowledge the duplicate guard the way an operator does —
# a previous run of this very script is a legitimate near-duplicate (edge 23).
fd.post("/registration/new", {
    "FullName": NAME, "Sex": "M", "AgeOrDob": "50", "Phone": f"017{stamp}{stamp[:3]}",
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(fd.get("/api/typeahead/patients?q=" + urllib.parse.quote(NAME)))
fixture(hits or None, "the desk-check patient did not register",
        "the registration screen refused the fixture — run lifecycle-thread.py step 1")
pid = str(hits[0]["value"])         # newest first; repeats from an earlier run are fine
record("patient", pid)

# --- LC-FD-05: the ward census on the panel, not on another screen -------------
case("LC-FD-05", "Beds free now — the panel's numbers track the ward", fd)

before = beds_panel(fd.get("/frontdesk"))
check(len(before) > 0,
      f"the beds panel renders at least one class row (got {len(before)})")
check(all(0 <= free <= total for free, total in before.values()) and any(
          total > 0 for _free, total in before.values()),
      f"every class reads 0 <= free <= total, and the ward is not empty — {before}")

admit = fd.get("/ipd/admit")
# Capture the bed AND its class from the same option, so the assertion below names the class the
# admission actually consumes rather than assuming "general".
# The ward NAME carries brackets of its own ("General Ward (Female)"), so the class is taken
# from the last parenthesised group on the option rather than the first.
opt = fixture(re.search(r'<option value="(\d+)">(GW[MF]-\d+) — [^<]*\(([a-z]+), [^<]*/day\)',
                        admit),
              "no free general-ward bed — every ward bed is occupied",
              "a thread that admits must discharge; reset the database if a run was killed")
bed_id, bed_code, bed_class = opt.group(1), opt.group(2), opt.group(3).upper()
check(bed_class in before,
      f"the panel names the class the admit screen offers — {bed_code} is {bed_class}")

url, _ = fd.post("/ipd/admit", {
    "PatientId": pid, "Source": "direct", "BedId": bed_id,
    "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
adm = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused").group(1)
record("admission", adm)


# The absconded exit and the housekeeping below are the last two statements of the happy path;
# a failure on any assertion between here and there used to keep the bed (spec 0029 F3).
def _return_bed():
    fd.post(f"/ipd/folio/{adm}?handler=Absconded", {"AdmissionId": adm},
            fd.get(f"/ipd/folio/{adm}"))
    release_bed(fd, bed_id)


on_exit(f"admission {adm} closed and bed returned", _return_bed)

after = beds_panel(fd.get("/frontdesk"))
was_free, was_total = before.get(bed_class, (0, 0))
now_free, now_total = after.get(bed_class, (-1, -1))
check(now_free == was_free - 1,
      f"admitting into {bed_class} took exactly one bed off the panel "
      f"(free {was_free} -> {now_free})")
check(now_total == was_total,
      f"{bed_class} total is unchanged by an admission ({was_total} -> {now_total})")
check(all(after[c] == before[c] for c in before if c != bed_class),
      "no other class moved")

# --- LC-FD-01/02/03: the estimate ---------------------------------------------
case("LC-FD-01", "Live estimate includes unposted bed days", fd)
# Front desk BEFORE any folio view by money-side staff: fee 500 posted, bed day 1200 accrued.
desk = fd.get(f"/frontdesk?patientId={pid}")
check(estimate(desk) == 1700,
      f"estimate = fee 500 posted + bed 1200 accrued (got {estimate(desk)})")
check("This estimate is incomplete" not in desk,
      "a fully priced stay quotes a figure, not a floor (M2-D1)")

case("LC-FD-02", "Advance subtracted from the estimate", op)
sess = op.get("/billing/session")
if "Your counter is open" not in sess:
    counter = re.search(r'value="(\d+)"[^>]*>[^<]*Front Desk', sess)
    op.post("/billing/session", {"CounterId": counter.group(1) if counter else "1",
                                 "OpeningFloat": "500"}, sess)
folio = op.get(f"/ipd/folio/{adm}")          # rasel's view catches the bed day up (0017 rule)
op.post(f"/ipd/folio/{adm}?handler=Advance",
        {"AdmissionId": adm, "AdvanceAmount": "700", "AdvanceTender": "cash"}, folio)

desk = fd.get(f"/frontdesk?patientId={pid}")
check(estimate(desk) == 1000, f"estimate = 1700 posted − 700 advance (got {estimate(desk)})")

case("LC-FD-03", "Reading the screen twice changes nothing", fd)
desk2 = fd.get(f"/frontdesk?patientId={pid}")
check(estimate(desk2) == estimate(desk) == 1000,
      "a second read changes nothing (read-only)")
check(desk2.count("Net payable now") == desk.count("Net payable now"),
      "the second read renders the same single estimate row")

# --- LC-FD-06: today's doctors -------------------------------------------------
case("LC-FD-06", "Today's doctors — booked / waiting / done track the queue", fd)

doctors_before = doctors_panel(fd.get("/frontdesk"))
check(len(doctors_before) > 0,
      f"the doctors panel renders at least one doctor (got {len(doctors_before)})")

appts = fd.get("/appointments")
doc = fixture(re.search(r'<option value="(\d+)">([^<]*?) — next serial \d+</option>', appts),
              "no doctor offers a serial on /appointments today",
              "seed a doctor schedule for today — see DevSeed.cs")
doctor_id, doctor_name = doc.group(1), doc.group(2).strip()
check(doctor_name in doctors_before,
      f"the doctor taking the serial is on the help desk panel — {doctor_name}")

b0, w0, d0 = doctors_before.get(doctor_name, (0, 0, 0))
fd.post("/appointments?handler=Issue", {"PatientId": pid, "DoctorId": doctor_id}, appts)

b1, w1, d1 = doctors_panel(fd.get("/frontdesk")).get(doctor_name, (-1, -1, -1))
check(b1 == b0 + 1, f"{doctor_name}: booked {b0} -> {b1} after one serial")
check(w1 == w0 + 1, f"{doctor_name}: waiting {w0} -> {w1} — the patient is in the queue")
check(d1 == d0, f"{doctor_name}: done is unchanged by issuing ({d0} -> {d1})")


def my_serial(queue_html):
    """The `(id, state)` of THIS run's serial — matched by patient, not by position."""
    for block in queue_html.split("<tr>"):
        if NAME in block:
            m = re.search(r'name="id" value="(\d+)"\s*/>\s*'
                          r'<input type="hidden" name="from" value="(\w+)"', block)
            if m:
                return m.group(1), m.group(2)
    return None, None


# The operator's own path is booked -> in chamber -> done, so walk that, not a shortcut.
queue = fd.get("/appointments")
serial_id, state = my_serial(queue)
if serial_id is None:
    check(False, "the issued serial is not on /appointments — cannot walk the queue")
else:
    fd.post("/appointments?handler=Advance",
            {"id": serial_id, "from": state, "to": "in_chamber"}, queue)
    b2, w2, d2 = doctors_panel(fd.get("/frontdesk")).get(doctor_name, (-1, -1, -1))
    # A patient called into the chamber has not been seen yet — the desk must still say waiting.
    check((b2, w2, d2) == (b1, w1, d1),
          f"{doctor_name}: calling the patient in changes nothing on the desk "
          f"— in-chamber still counts as waiting ({b2}/{w2}/{d2})")

    queue = fd.get("/appointments")
    fd.post("/appointments?handler=Advance",
            {"id": serial_id, "from": "in_chamber", "to": "done"}, queue)
    b3, w3, d3 = doctors_panel(fd.get("/frontdesk")).get(doctor_name, (-1, -1, -1))
    check(d3 == d1 + 1, f"{doctor_name}: done {d1} -> {d3} once the patient is seen")
    check(w3 == w1 - 1, f"{doctor_name}: waiting {w1} -> {w3} — no longer waiting")
    check(b3 == b1, f"{doctor_name}: booked is unchanged by completing ({b1} -> {b3})")

# --- teardown: hand the bed back, then prove the panel noticed -----------------
_return_bed()

case("LC-FD-05", "Beds free now — releasing the bed restores the census", fd)
restored = beds_panel(fd.get("/frontdesk"))
check(restored.get(bed_class, (None, None))[0] == was_free,
      f"{bed_class} free is back to {was_free} (got {restored.get(bed_class, ('?', '?'))[0]})")

sys.exit(report("FRONT DESK / HELP DESK"))
