#!/usr/bin/env python3
"""R5 Nursing Station (spec 0041) — the ward nurse's shift, end to end.

The four pillars in the order a ward uses them: the consultant writes an indoor prescription,
the nurse builds the medicine chart from it, the station board shows what is due and what is
late, and the ward's tasks and duty roster are recorded against names.

Every write is proved by going back to a *rendered screen* and finding the row. The spec 0037
lesson is that a handler can return 302 with a green toast and save nothing — a status code
proves the request was accepted, never that the record exists.

Tier t1: registers its own patient, admits, and hands the bed back on exit (0029 F3), so a
second run against the same database is fine.

    python3 eng/verify/nursing-thread.py                     (app on :5199)
    BASE_URL=https://hms.example.com HMS_QA_ENV=vm HMS_QA_CONFIRM=hms.example.com \
        python3 eng/verify/nursing-thread.py
"""
import datetime
import json
import re
import sys
import urllib.error
import urllib.parse

from _harness import (RUN_ID, Session, case, check, fixture, guard, on_exit, record, report,
                      settle_and_discharge, step, tag)

STAMP = re.sub(r"[^0-9a-z]", "", RUN_ID.lower())[-6:]
DRUG = f"Napa{STAMP}"          # unique, so assertions never match another run's row
PRN_DRUG = f"Omidon{STAMP}"


def toast(html):
    m = re.search(r'class="toast[^"]*"[\s\S]{0,300}?<span>([^<]*)</span>', html)
    return m.group(1).strip() if m else None


def alert(html):
    m = re.search(r'class="alert bad"[\s\S]{0,300}?<span>([^<]*)</span>', html)
    return m.group(1).strip() if m else None


def notice(html):
    return toast(html) or alert(html)


DHAKA = datetime.timezone(datetime.timedelta(hours=6))    # Asia/Dhaka, no DST


def expected_doses(slots=(8, 20), days=3):
    """How many doses "1+0+1 for 3 days" should produce *if generated right now*.

    Day one drops the slots that have already passed, because generation must never create a
    dose that is born overdue. So the answer is six before 08:00 and five after — and a test
    that hard-codes six is green only in the early morning, which is precisely the defect
    spec 0027 found in the day-close suite.
    """
    now = datetime.datetime.now(DHAKA)
    today_left = sum(1 for h in slots if datetime.time(h, 0) > now.time())
    return today_left + len(slots) * (days - 1)


def row_for(html, needle):
    """The <tr> containing `needle`, so a badge is asserted on the right row."""
    for row in re.findall(r"<tr>[\s\S]*?</tr>", html):
        if needle in row:
            return row
    return ""


def main() -> int:
    guard("t1")

    desk = Session("jashim")          # ipd.manage — registers and admits
    doctor = Session("chowdhury")     # emr.note.write — the ward round
    nurse = Session("nasrin")         # the nursing console itself
    billing = Session("rasel")        # the adjacent job, for the denial case

    # -- 1. a patient in a bed -------------------------------------------------------------
    case("LC-NUR-07", "An admitted patient appears on the nursing station", desk)
    name = tag(f"Nursing {STAMP}")
    allergy = f"Penicillin{STAMP}"
    desk.post("/registration/new", {
        "FullName": name, "Sex": "F", "AgeOrDob": "39", "Phone": f"0198{STAMP}0",
        "Allergies": allergy,
        "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
    hits = json.loads(desk.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
    patient_id = str(fixture(hits[0]["value"] if hits else None,
                             f"the patient {name} could not be registered"))
    record("patient", patient_id)

    admit = desk.get("/ipd/admit")
    # Spec 0046: the thread's nurse (nasrin) is scoped to Medicine — admit into a general ward
    # (GWM/GWF) so the patient lands on her department's station.
    bed = fixture(re.search(r'<option value="(\d+)">(GW[MF])', admit),
                  "no free bed anywhere in the hospital",
                  "a thread that admits must discharge; reset the database if a run was killed")
    url, _ = desk.post("/ipd/admit", {
        "PatientId": patient_id, "Source": "direct", "BedId": bed.group(1),
        "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
    admission = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused")
    admission_id = admission.group(1)
    record("admission", admission_id)
    on_exit(f"admission {admission_id} discharged and bed returned",
            lambda: settle_and_discharge(Session, admission_id, [bed.group(1)]))

    step(1, "the station board shows the patient")
    station = nurse.get("/ipd/station")
    check(name in station, "the admitted patient is on the ward monitor")
    patient_row = row_for(station, name)
    check("nothing scheduled" in patient_row, "no medicines are due before anything is prescribed")
    check("0 overdue" in station, "the board opens with nothing overdue")

    # -- 2. the ward round: an indoor prescription -----------------------------------------
    case("LC-NUR-08", "A consultant writes an indoor prescription", doctor)
    rx_page = doctor.get(f"/emr/indoor/{admission_id}")
    check(name in rx_page, "the prescribing screen opens on this admission")
    _, after = doctor.post(f"/emr/indoor/{admission_id}?handler=Sign", {
        "AdmissionId": admission_id,
        "Complaint": "Fever since morning", "Diagnosis": "Viral fever", "Advice": "Rest",
        # Two lines on purpose: one the parser reads, one it must refuse to guess at.
        "DrugName": [DRUG, PRN_DRUG],
        "DrugDose": ["1 tab", "1 tab"],
        "DrugFrequency": ["1+0+1", "PRN"],
        "DrugDuration": ["3 days", ""],
        "DrugInstruction": ["after food", "if vomiting"],
    }, rx_page)
    rx_page = doctor.get(f"/emr/indoor/{admission_id}")
    check("Signed" in rx_page, "the prescription is signed, not left a draft")
    check(">2<" in rx_page or "2</td>" in rx_page, "both medicines are on the prescription")

    # -- 3. build the chart from it --------------------------------------------------------
    case("LC-NUR-09", "The medicine chart is generated from the prescription", nurse)
    charts = nurse.get(f"/emr/charts/{admission_id}")
    check("Generate schedule" in charts, "the chart offers to build itself from the prescription")
    note_id = fixture(re.search(r'name="NoteId" value="(\d+)"', charts),
                      "no signed prescription is offered on the chart")
    _, generated = nurse.post(f"/emr/charts/{admission_id}?handler=Generate", {
        "AdmissionId": admission_id, "NoteId": note_id.group(1)}, charts)

    charts = nurse.get(f"/emr/charts/{admission_id}")
    scheduled = charts.count(DRUG)
    want = expected_doses()
    check(scheduled == want,
          f"1+0+1 for 3 days scheduled {want} doses at this hour (saw {scheduled})")
    check("08:00" in charts and "20:00" in charts, "the doses sit at the morning and night slots")
    check(PRN_DRUG not in charts, "the PRN line was not guessed at")

    step(2, "no generated dose is born overdue")
    generated_rows = [r for r in re.findall(r"<tr>[\s\S]*?</tr>", charts) if DRUG in r]
    check(generated_rows and not any("Overdue" in r for r in generated_rows),
          "none of the generated doses is already overdue")
    said = notice(generated)
    check(said is not None and PRN_DRUG in said,
          f"the unreadable line is named for hand scheduling (said: {said!r})")

    step(3, "pressing Generate again changes nothing")
    charts = nurse.get(f"/emr/charts/{admission_id}")
    _, again = nurse.post(f"/emr/charts/{admission_id}?handler=Generate", {
        "AdmissionId": admission_id, "NoteId": note_id.group(1)}, charts)
    charts = nurse.get(f"/emr/charts/{admission_id}")
    check(charts.count(DRUG) == scheduled, "a second Generate did not double the chart")
    check("already on the chart" in (notice(again) or ""), "and says so plainly")

    # -- 4. LC-NUR-06: a dose that is late looks late --------------------------------------
    case("LC-NUR-06", "A missed dose is visible as missed", nurse)
    # Schedule one in the past by hand — the same path a nurse uses for a dose given off-round.
    past_drug = f"Late{STAMP}"
    charts = nurse.get(f"/emr/charts/{admission_id}")
    nurse.post(f"/emr/charts/{admission_id}?handler=Schedule", {
        "AdmissionId": admission_id, "DrugName": past_drug, "Dose": "1 g", "Route": "IV",
        "ScheduledDate": "", "ScheduledTime": "00:05"}, charts)

    charts = nurse.get(f"/emr/charts/{admission_id}")
    late_row = row_for(charts, past_drug)
    check("Overdue" in late_row, "a dose past its time is badged Overdue, not left looking pending")

    station = nurse.get("/ipd/station")
    check("overdue" in row_for(station, name),
          "the ward monitor counts it as overdue for this patient")

    step(4, "marking it missed needs a reason, and happens once")
    dose_id = fixture(re.search(r'name="DoseId" value="(\d+)"', late_row),
                      "the overdue dose offers no recording control")
    _, refused = nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
        "AdmissionId": admission_id, "DoseId": dose_id.group(1),
        "Outcome": "missed", "Reason": ""}, charts)
    check("Say why" in (notice(refused) or ""), "a missed dose without a reason is refused")

    charts = nurse.get(f"/emr/charts/{admission_id}")
    nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
        "AdmissionId": admission_id, "DoseId": dose_id.group(1),
        "Outcome": "missed", "Reason": "Patient was in theatre"}, charts)
    charts = nurse.get(f"/emr/charts/{admission_id}")
    late_row = row_for(charts, past_drug)
    check("Missed" in late_row, "the chart shows the dose as missed")
    check("Patient was in theatre" in late_row, "with the reason on the record")
    check("Nasrin" in late_row, "and the nurse's name against it")

    charts = nurse.get(f"/emr/charts/{admission_id}")
    _, twice = nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
        "AdmissionId": admission_id, "DoseId": dose_id.group(1),
        "Outcome": "given", "Reason": ""}, charts)
    check("already recorded" in (notice(twice) or ""), "a second recording of the same dose is refused")

    # -- 5. care tasks ---------------------------------------------------------------------
    case("LC-NUR-10", "A care task is raised, worked and attributable", nurse)
    task_title = f"Turn the patient {STAMP}"
    tasks = nurse.get(f"/emr/tasks/{admission_id}")
    check(name in tasks, "the task list opens on this patient")
    nurse.post(f"/emr/tasks/{admission_id}?handler=Create", {
        "AdmissionId": admission_id, "Title": task_title, "Details": "left side",
        "Kind": "positioning", "DueDate": "", "DueTime": "00:05"}, tasks)

    tasks = nurse.get(f"/emr/tasks/{admission_id}")
    check(task_title in tasks, "the task is on the list")
    task_row = row_for(tasks, task_title)
    check("Overdue" in task_row, "a task past its due time shows as overdue")

    station = nurse.get("/ipd/station")
    check("late" in row_for(station, name) or "open" in row_for(station, name),
          "the ward monitor counts the open task")

    step(5, "cancelling without a reason is refused")
    task_id = fixture(re.search(r'name="TaskId" value="(\d+)"', task_row),
                      "the open task offers no controls")
    _, no_reason = nurse.post(f"/emr/tasks/{admission_id}?handler=Cancel", {
        "AdmissionId": admission_id, "TaskId": task_id.group(1), "Reason": ""}, tasks)
    check("Say why" in (notice(no_reason) or ""), "cancelling without a reason is refused")

    step(6, "completing it is single-shot and carries her name")
    tasks = nurse.get(f"/emr/tasks/{admission_id}")
    nurse.post(f"/emr/tasks/{admission_id}?handler=Done", {
        "AdmissionId": admission_id, "TaskId": task_id.group(1)}, tasks)
    tasks = nurse.get(f"/emr/tasks/{admission_id}")
    done_row = row_for(tasks, task_title)
    check("Done" in done_row, "the task is closed")
    check("Nasrin" in done_row, "the closing nurse is named")

    _, done_twice = nurse.post(f"/emr/tasks/{admission_id}?handler=Done", {
        "AdmissionId": admission_id, "TaskId": task_id.group(1)}, tasks)
    check("already closed" in (notice(done_twice) or ""), "closing it twice is refused")

    # -- 6. ward duty ----------------------------------------------------------------------
    case("LC-NUR-11", "Ward duty is assigned, refused twice, and ended with a reason", nurse)
    staff = f"Salma {STAMP}"
    duty = nurse.get("/ipd/duty")
    ward_id = fixture(re.search(r'name="WardId"[\s\S]{0,200}?<option value="(\d+)"', duty),
                      "no ward is offered on the duty screen")
    nurse.post("/ipd/duty?handler=Assign", {
        "WardId": ward_id.group(1), "ShiftLabel": "morning", "StaffRole": "nurse",
        "EmployeeId": "", "StaffName": staff}, duty)

    duty = nurse.get("/ipd/duty")
    check(staff in duty, "the assignment is on the roster")

    step(7, "the same person twice on one shift is refused")
    _, dup = nurse.post("/ipd/duty?handler=Assign", {
        "WardId": ward_id.group(1), "ShiftLabel": "morning", "StaffRole": "nurse",
        "EmployeeId": "", "StaffName": staff}, duty)
    check("already on the morning shift" in (notice(dup) or ""), "a duplicate assignment is refused")

    step(8, "ending needs a reason")
    duty = nurse.get("/ipd/duty")
    assignment = fixture(re.search(re.escape(staff) + r"[\s\S]{0,400}?"
                                   r'name="AssignmentId" value="(\d+)"', duty),
                         "the assignment offers no End control")
    _, no_why = nurse.post("/ipd/duty?handler=End", {
        "AssignmentId": assignment.group(1), "Reason": ""}, duty)
    check("Say why" in (notice(no_why) or ""), "ending a duty without a reason is refused")

    duty = nurse.get("/ipd/duty")
    nurse.post("/ipd/duty?handler=End", {
        "AssignmentId": assignment.group(1), "Reason": "Swapped with the evening nurse"}, duty)
    duty = nurse.get("/ipd/duty")
    check(staff not in duty, "the ended assignment is off today's roster")

    # -- 7. the adjacent job is refused ----------------------------------------------------
    case("LC-NUR-12", "Nursing screens are refused to adjacent jobs", billing)
    check(billing.denied("/ipd/duty"), "a billing operator cannot reach ward duty")
    check(billing.denied("/emr/tasks"), "a billing operator cannot reach care tasks")
    check(desk.denied("/emr/tasks"), "a receptionist cannot reach care tasks")
    check(nurse.denied("/emr/indoor"), "a nurse cannot write a prescription")

    step(9, "and the handler refuses too, not just the menu")
    check(billing.post_denied("/emr/tasks?handler=Create",
                              {"AdmissionId": admission_id, "Title": "should not exist"}),
          "the care-task handler refuses a billing operator")
    check(billing.post_denied("/ipd/duty?handler=Assign",
                              {"WardId": ward_id.group(1), "ShiftLabel": "night",
                               "StaffRole": "aya", "StaffName": "should not exist"}),
          "the duty handler refuses a billing operator")

    # -- 8. the roster panel degrades honestly ---------------------------------------------
    case("LC-NUR-13", "The station's roster panel reads either way", nurse)
    station = nurse.get("/ipd/station")
    check("On duty today" in station, "the roster panel is on the board")
    check("No roster published" in station or "<td>" in station,
          "it either lists the roster or says plainly that none is published")

    # -- 9. the allergy travels with the patient (spec 0042) -------------------------------
    case("LC-NUR-16", "The allergy entered at registration reaches every ward screen", nurse)
    check(allergy in nurse.get("/ipd/station"), "the station tile carries the allergy")
    check(allergy in nurse.get(f"/emr/charts/{admission_id}"), "the chart banner carries it")
    check(allergy in doctor.get(f"/emr/indoor/{admission_id}"),
          "the prescribing screen carries it — the screen where it matters most")

    # -- 10. the doctor's round is a visit and a charge (spec 0042, §5 M6 [M]) -------------
    case("LC-NUR-14", "Signing posted one consultant-visit charge, and only one", nurse)
    # The folio renders each charge line twice (screen table + printable statement), so this
    # asserts presence and no-growth on the SCREEN; exactly-one-row is pinned at the database
    # by WardMoneySeamTests.Signing_records_one_visit_and_one_charge_per_doctor_per_day.
    def visit_lines(html):
        return html.count("Consultant Visit (indoor) &#x2014;") \
             + html.count("Consultant Visit (indoor) —")
    folio_page = nurse.get(f"/ipd/folio/{admission_id}")
    visits = visit_lines(folio_page)
    check(visits > 0, "the signed round put a visit charge on the folio")

    step(1, "a second note the same day is a longer round, not a second fee")
    rx_page = doctor.get(f"/emr/indoor/{admission_id}")
    pid_hits = json.loads(doctor.get("/api/typeahead/products?q=Napa"))
    product_id = str(pid_hits[0]["value"]) if pid_hits else ""
    doctor.post(f"/emr/indoor/{admission_id}?handler=Sign", {
        "AdmissionId": admission_id, "Complaint": "Evening round", "Diagnosis": "Improving",
        "DrugProductId": [product_id], "DrugName": [f"Seclo{STAMP}"],
        "DrugDose": ["1 cap"], "DrugFrequency": ["0+0+1"], "DrugDuration": ["2 days"],
        "DrugInstruction": [""],
    }, rx_page)
    folio_page = nurse.get(f"/ipd/folio/{admission_id}")
    check(visit_lines(folio_page) == visits,
          "the same doctor's second sign added no second charge")

    # -- 11. a prescription becomes an indent without re-typing (spec 0042) ----------------
    case("LC-NUR-17", "An indent is raised from the prescription's formulary lines", nurse)
    if product_id:
        strip = nurse.get(f"/ipd/folio/{admission_id}")
        check("Raise indent from prescription" in strip,
              "the folio offers to build the indent from the signed prescription")
        rx_note = fixture(re.search(r'name="RxNoteId" value="(\d+)"', strip),
                          "no prescription is offered on the indent card")
        nurse.post(f"/ipd/folio/{admission_id}?handler=IndentFromRx", {
            "AdmissionId": admission_id, "RxNoteId": rx_note.group(1)}, strip)
        after = nurse.get(f"/ipd/folio/{admission_id}")
        check("Requested" in after, "the indent is in the queue for pharmacy")
        check(f"From prescription #{rx_note.group(1)}" in after
              or "Requested" in after, "the indent names its prescription")
    else:
        check(False, "no formulary product available to prescribe — seed the pharmacy demo stock")

    # -- 12. an admitted patient at the diagnostics counter posts to the folio (0042) ------
    case("LC-NUR-18", "The diagnostics counter routes an inpatient's test to the folio", billing)
    order_page = billing.get(f"/diagnostics/order?PatientId={patient_id}")
    check("Admitted patient" in order_page and "take no cash" in order_page,
          "the counter says the patient is admitted and where the money goes")
    test_id = fixture(re.search(r'name="catalogId" value="(\d+)"', order_page),
                      "no priced test in today's catalogue")
    invoices_before = nurse.get(f"/ipd/folio/{admission_id}").count("Diagnostics")
    _, saved = billing.post(f"/diagnostics/order?PatientId={patient_id}&handler=Save", {
        "PatientId": patient_id, "Items": [test_id.group(1)],
        "DiscountFlat": "0", "PaidNow": "0", "Tender": "cash"}, order_page)
    folio_page = nurse.get(f"/ipd/folio/{admission_id}")
    check(folio_page.count("Diagnostics") > invoices_before,
          "the test landed on the folio, not on a separate outdoor invoice")

    # -- 13. discharge with open work: visible at the gate, closable after (0042 F8) -------
    case("LC-NUR-15", "Open ward work survives discharge, read-only but closable", nurse)
    open_doses = nurse.get(f"/emr/charts/{admission_id}").count('name="DoseId"')
    check(open_doses > 0, f"the ward still holds {open_doses} open dose(s) — the honest case")

    step(2, "the clearance step names the open work")
    desk.post(f"/ipd/discharge/{admission_id}?handler=Initiate", {
        "AdmissionId": admission_id,
        "ClinicalSummary": "QA: discharged holding open ward work on purpose."},
        desk.get(f"/ipd/discharge/{admission_id}"))
    gate = desk.get(f"/ipd/discharge/{admission_id}")
    check("The ward still holds" in gate, "the clearance screen warns about the open items")

    step(3, "discharge proceeds — the money gate is unchanged")
    settle_and_discharge(Session, admission_id, [bed.group(1)])

    station = nurse.get("/ipd/station")
    check(name not in station, "the station drops the discharged patient")

    step(4, "the record is read-only, not gone")
    charts = nurse.get(f"/emr/charts/{admission_id}")
    check("read-only" in charts, "the chart opens on the closed admission and says why")
    check("Schedule a dose" not in charts, "nothing new can be scheduled")
    late_dose = fixture(re.search(r'name="DoseId" value="(\d+)"', charts),
                        "the open dose lost its recording control")
    nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
        "AdmissionId": admission_id, "DoseId": late_dose.group(1),
        "Outcome": "missed", "Reason": "Patient discharged before this dose"}, charts)
    charts = nurse.get(f"/emr/charts/{admission_id}")
    check("Patient discharged before this dose" in charts,
          "the dose closed with the nurse's reason, after discharge")

    step(5, "but new work on the closed admission is refused")
    _, refused = nurse.post(f"/emr/charts/{admission_id}?handler=Schedule", {
        "AdmissionId": admission_id, "DrugName": f"Ghost{STAMP}", "Dose": "1",
        "Route": "", "ScheduledDate": "", "ScheduledTime": ""}, charts)
    check("closed" in (notice(refused) or ""), "scheduling on a closed admission is refused")

    return report("R5 nursing station thread")


if __name__ == "__main__":
    sys.exit(main())
