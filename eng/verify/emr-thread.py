#!/usr/bin/env python3
"""Spec 0024 — M5 Prescription & EMR, end to end against a running app.

Walks the whole clinical thread the way the people do it: the counter opens a visit, the nurse
takes vitals, the consultant writes and signs a prescription with drugs and tests, the tests
turn up unbilled at the billing counter (US5.4), the record shows the visit afterwards, and a
correction supersedes without erasing (§10).

Dirty-database tolerant: registers its own patient, and asserts about its own records only.

usage: python3 eng/verify/emr-thread.py       (from the repo root, app on :5199)
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

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import fixture, on_exit, record, settle_and_discharge, tag   # noqa: E402

# Overridable so the same thread can be run against a deployed instance
# (`BASE_URL=https://… python3 …`) after a release, the way the others are run locally.
BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')


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

    def get_url(self, path):
        with self.op.open(BASE + path) as r:
            return r.geturl(), r.read().decode()

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


stamp = f"{int(time.time() * 1000) % 100000:05d}"
print("EMR / PRESCRIPTION THREAD (M5)")

desk = Session("jashim")
counter = Session("rasel")
nurse = Session("nasrin")
doctor = Session("chowdhury")

# ---- 1. a patient and a paid visit ------------------------------------------------
print("\n1. the counter opens a visit")
name = tag(f"EMR Test {stamp}")
desk.post("/registration/new", {
    "FullName": name, "Sex": "M", "AgeOrDob": "41", "Phone": f"01755{stamp}0",
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save",
})
hits = json.loads(desk.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
check(len(hits) > 0, f"patient registered ({name})")
if not hits:
    sys.exit(1)
patient_id = str(hits[0]["value"])
uhid = re.search(r"ALT-\d{6}", hits[0]["label"]).group(0)

# The counter needs an open session before it can bill.
session_html = counter.get("/billing/session")
if "Your counter is open" not in session_html:
    counter.post("/billing/session", {"CounterId": "1", "OpeningFloat": "1000"}, session_html)
check("Your counter is open" in counter.get("/billing/session"), "the billing counter is open")

opd = counter.get("/billing/opd")
opd = counter.post("/billing/opd", {"PatientId": patient_id}, opd)[1]
rows = re.findall(r'<span class="cat-code">([A-Z\-0-9]+)</span>.*?name="catalogId" value="(\d+)"',
                  opd, re.S)
catalogue = {code: cid for code, cid in rows}
check(len(catalogue) > 0, f"service catalogue priced ({len(catalogue)} items)")
consult_service = catalogue.get("CON-SPC") or next(iter(catalogue.values()))
opd = counter.post("/billing/opd", {"PatientId": patient_id, "Items": [consult_service],
                                    "catalogId": consult_service, "handler": "Add"}, opd)[1]
gross = re.search(r'data-gross="(\d+)"', opd)
url, html = counter.post("/billing/opd?handler=Save", {
    "PatientId": patient_id, "Items": [consult_service],
    "PaidNow": gross.group(1) if gross else "0", "Tender": "cash"}, opd)
if "/billing/invoice/" not in url:
    problem = re.search(r'class="alert bad">.*?<span>(.*?)</span>', html, re.S)
    print("     billing said:", (problem.group(1).strip()[:160] if problem else "no message"))
check("/billing/invoice/" in url, f"consultation billed — the visit exists ({url.split('/')[-1]})")

# ---- 2. the nurse takes vitals ----------------------------------------------------
print("\n2. the nurse records pre-checkup vitals (US5.3)")
vitals_page = nurse.get("/emr/vitals")
encounter_ids = re.findall(r'<option value="(\d+)">([^<]*)</option>', vitals_page)
mine = [eid for eid, label in encounter_ids if uhid in label]
check(len(mine) == 1, f"our visit is on the nurse's list ({len(mine)} match)")
if not mine:
    sys.exit(1)
encounter_id = mine[0]

nurse.post("/emr/vitals", {
    "EncounterId": encounter_id, "Systolic": "138", "Diastolic": "86", "Pulse": "82",
    "Temperature": "37.4", "Weight": "71.5", "SpO2": "97",
}, vitals_page)
after = nurse.get("/emr/vitals")
check("BP 138/86" in after, "the reading is on the board as BP 138/86")
# The degree sign is HTML-escaped in the page, so assert on the number itself.
check("37.4" in after, "37.4 °C survived the round trip (tenths, not floats)")

# ---- 3. the doctor sees it and writes the prescription -----------------------------
print("\n3. the consultant writes and signs (US5.1, US5.2)")
queue = doctor.get("/emr/queue")
check(uhid in queue, "the patient is on the consultation queue")
check("BP 138/86" in queue, "the nurse's vitals are already there — nothing to fetch")

consult = doctor.get(f"/emr/consult/{encounter_id}")
check("BP 138/86" in consult, "vitals are on the consultation screen")
tests = re.findall(r'name="Tests" value="(\d+)"', consult)
check(len(tests) > 0, f"the test catalogue is on the screen ({len(tests)} tests)")

url, html = doctor.post(f"/emr/consult/{encounter_id}?handler=Finalise", {
    "EncounterId": encounter_id,
    "Complaint": "Fever and sore throat, 3 days",
    "OnExamination": "Throat congested, no exudate",
    "Diagnosis": f"Acute pharyngitis [{stamp}]",
    "Advice": "Warm saline gargle; review if fever persists",
    "FollowUp": "7 days",
    "DrugProductId": ["", ""],
    "DrugName": ["Napa 500 mg", "Fexo 120 mg"],
    "DrugDose": ["1 tab", "1 tab"],
    "DrugFrequency": ["1+1+1", "0+0+1"],
    "DrugDuration": ["5 days", "7 days"],
    "DrugInstruction": ["after food", ""],
    "Tests": tests[:2],
}, consult)
check("/emr/prescription/" in url, f"signing lands on the printable prescription ({url.split('/')[-1]})")
note_id = url.rstrip("/").split("/")[-1]

check("Acute pharyngitis" in html, "the diagnosis is on the sheet")
check("Napa 500 mg" in html and "Fexo 120 mg" in html and "5 days" in html,
      "both medicines print with dose and duration")
check("Dr. A. K. Chowdhury" in html, "the sheet carries the prescriber's name")

# ---- 4. the ordered tests reach the counter (US5.4 AC) -----------------------------
print("\n4. the ordered tests are waiting at the billing counter — nothing re-typed")
# The counter's screen carries the doctor's charges without anyone typing them (US5.4 AC).
opd2 = counter.get("/billing/opd")
counter_html = counter.post("/billing/opd", {"PatientId": patient_id}, opd2)[1]
carried = re.findall(r'<span class="pill info"[^>]*>from ([A-Za-z]+)</span>', counter_html)
check("Diagnostics" in carried,
      f"the counter shows charges carried in from Diagnostics ({carried})")
ordered_test_names = [n for _, n in
                      re.findall(r'name="Tests" value="(\d+)" /> ([^<\n]+)', consult)][:2]
check(all(t.strip() in counter_html for t in ordered_test_names),
      f"the ordered tests appear by name at the counter ({[t.strip() for t in ordered_test_names]})")

consult_after = doctor.get(f"/emr/consult/{encounter_id}")
check("Investigations raised on this visit" in consult_after and "Ordered" in consult_after,
      "the consultation screen still shows the raised order after signing")

# ---- 5. the record ----------------------------------------------------------------
print("\n5. the longitudinal record")
history = doctor.get(f"/emr/history/{patient_id}")
check("Acute pharyngitis" in history, "the visit is in the patient's record")
check("Napa 500 mg" in history, "so are the medicines")

# ---- 6. immutability and correction (§10) ------------------------------------------
print("\n6. a signed prescription is corrected, never edited")
consult_signed = doctor.get(f"/emr/consult/{encounter_id}")
check("This prescription is signed" in consult_signed,
      "the consultation screen offers no edit once signed (U7)")
check('name="Complaint"' not in consult_signed,
      "the editing form is absent, not merely disabled")

sheet = doctor.get(f"/emr/prescription/{note_id}")
url, corrected = doctor.post(f"/emr/prescription/{note_id}?handler=Correct", {}, sheet)
check("/emr/consult/" in url, "the correction opens as a new draft")

original = doctor.get(f"/emr/prescription/{note_id}")
check("was corrected" in original, "the original says it was corrected")
check("Acute pharyngitis" in original, "and still carries exactly what was signed")

# ---- 7. the ward's own paperwork (5A-7) --------------------------------------------
print("\n7. nursing charts on an admitted patient (5A-7)")
admit = desk.get("/ipd/admit")
bed = fixture(re.search(r'<option value="(\d+)">(GW[MF]|CAB|ICU)', admit),
              "no free bed anywhere in the hospital",
              "a thread that admits must discharge; reset the database if a run was killed")
check(bed is not None, "a free bed is offered")
if bed:
    url, _ = desk.post("/ipd/admit", {
        "PatientId": patient_id, "Source": "opd", "BedId": bed.group(1),
        "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
    admission_id = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused")
    check(admission_id is not None, "the patient is admitted")

    if admission_id:
        admission_id = admission_id.group(1)
        record("admission", admission_id)
        # Spec 0029 F3: this thread used to admit and walk away. Four of the thirteen seeded
        # beds were held by EMR-thread patients admitted on previous days.
        on_exit(f"admission {admission_id} discharged and bed returned",
                lambda: settle_and_discharge(Session, admission_id, [bed.group(1)]))
        charts = nurse.get(f"/emr/charts/{admission_id}")
        check("Medicine chart (MAR)" in charts, "the MAR opens for this admission")

        nurse.post(f"/emr/charts/{admission_id}?handler=Schedule", {
            "AdmissionId": admission_id, "DrugName": f"Ceftriaxone {stamp}",
            "Dose": "1 g", "Route": "IV", "ScheduledDate": "", "ScheduledTime": ""}, charts)
        charts = nurse.get(f"/emr/charts/{admission_id}")
        check(f"Ceftriaxone {stamp}" in charts, "the dose is on the chart")

        dose_id = re.search(r'name="DoseId" value="(\d+)"', charts)
        check(dose_id is not None, "a waiting dose offers the recording control")
        if dose_id:
            # A dose not given needs a stated reason — the chart is a legal record.
            _, refused = nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
                "AdmissionId": admission_id, "DoseId": dose_id.group(1),
                "Outcome": "missed", "Reason": ""}, charts)
            check("Say why" in refused, "a missed dose without a reason is refused")

            nurse.post(f"/emr/charts/{admission_id}?handler=Administer", {
                "AdmissionId": admission_id, "DoseId": dose_id.group(1),
                "Outcome": "given", "Reason": ""}, charts)
            charts = nurse.get(f"/emr/charts/{admission_id}")
            check("Nasrin Sultana" in charts, "the chart says who gave it")
            check(f'name="DoseId" value="{dose_id.group(1)}"' not in charts,
                  "a recorded dose cannot be recorded twice (the control is gone)")

        nurse.post(f"/emr/charts/{admission_id}?handler=Glucose", {
            "AdmissionId": admission_id, "Glucose": "8.6", "Timing": "fasting",
            "InsulinUnits": "6", "InsulinRoute": "s/c"}, charts)
        charts = nurse.get(f"/emr/charts/{admission_id}")
        check("8.6" in charts, "the diabetic chart holds 8.6 mmol/L")
        check("6 u s/c" in charts, "with the insulin given")

        nurse.post(f"/emr/charts/{admission_id}?handler=Handover", {
            "AdmissionId": admission_id, "ReceivedFrom": "OPD",
            "Condition": "Conscious, ambulant", "Belongings": "One bag"}, charts)
        charts = nurse.get(f"/emr/charts/{admission_id}")
        check("Conscious, ambulant" in charts, "the receive note records the handover")

print()
if fail:
    print(f"EMR THREAD FAILED — {len(fail)} check(s)")
    for f in fail:
        print(f"  - {f}")
    sys.exit(1)
print("EMR THREAD PASSED")
