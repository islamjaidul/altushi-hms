#!/usr/bin/env python3
"""Spec 0026 — M10 Radiology, end to end against a running app.

Orders and pays for an imaging test, then proves the study reaches the right machine's worklist
and no other (US10.1), can be marked shot, reported against the exam template, printed
provisional while unsigned, signed once, and delivered through the existing M8 flow (US10.2 AC).

Dirty-database tolerant: registers its own patient and asserts about its own accession number.

usage: python3 eng/verify/radiology-thread.py     (from the repo root, app on :5199)
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
from _harness import record, tag   # noqa: E402

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
print("RADIOLOGY THREAD (M10)")

desk = Session("jashim")
counter = Session("rasel")
tech = Session("moinul")
radiologist = Session("farhana")

# ---- 1. a paid imaging order ------------------------------------------------------
print("\n1. an imaging order, paid at the counter")
name = tag(f"RAD Test {stamp}")
desk.post("/registration/new", {
    "FullName": name, "Sex": "M", "AgeOrDob": "50", "Phone": f"01777{stamp}0",
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(desk.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
check(len(hits) > 0, f"patient registered ({name})")
if not hits:
    sys.exit(1)
patient_id = str(hits[0]["value"])

session_html = counter.get("/billing/session")
if "Your counter is open" not in session_html:
    counter.post("/billing/session", {"CounterId": "1", "OpeningFloat": "1000"}, session_html)

order_page = counter.get(f"/diagnostics/order?PatientId={patient_id}")
row = re.search(r'<span class="cat-code">XRAY-CH</span>.*?name="catalogId" value="(\d+)".*?'
                r'data-price="(\d+)"', order_page, re.S)
if row is None:
    row = re.search(r'<span class="cat-code">XRAY-CH</span>.*?name="catalogId" value="(\d+)"',
                    order_page, re.S)
check(row is not None, "the X-ray is in the priced catalogue")
if row is None:
    sys.exit(1)
test_id = row.group(1)
price = re.search(r'data-price="(\d+)"', order_page)

url, html = counter.post("/diagnostics/order?handler=Save", {
    "PatientId": patient_id, "Items": [test_id], "DiscountFlat": "0",
    "PaidNow": "500", "Tender": "cash"}, order_page)
check("/diagnostics/order/" in url, f"the order is created and paid ({url.split('/')[-1]})")

# ---- 2. it reaches the right machine ----------------------------------------------
print("\n2. the study reaches the X-ray worklist and no other (US10.1)")
worklist = tech.get("/radiology/worklist")
check(name in worklist, "the study is on the combined worklist")
accession = re.search(rf'<td class="id-cell">(ACC-\d+)</td>\s*<td>{re.escape(name)}', worklist)
check(accession is not None, "and it carries an accession number")
accession = accession.group(1) if accession else ""

modalities = re.findall(r'/radiology/worklist\?ModalityId=(\d+)&ShowReported=\w+">([^<]+)</a>',
                        worklist)
xray = [mid for mid, label in modalities if label.strip() == "X-ray"]
usg = [mid for mid, label in modalities if label.strip() == "Ultrasound"]
check(len(xray) == 1 and len(usg) == 1, f"the machines are seeded ({len(modalities)} listed)")

on_xray = tech.get(f"/radiology/worklist?ModalityId={xray[0]}")
on_usg = tech.get(f"/radiology/worklist?ModalityId={usg[0]}")
check(accession in on_xray, "the X-ray worklist has it")
check(accession not in on_usg, "the ultrasound worklist does not")

# ---- 3. the technician marks it done ----------------------------------------------
print("\n3. the technician marks the study shot")
study_id = re.search(rf'{re.escape(accession)}.*?name="StudyId" value="(\d+)"', on_xray, re.S)
check(study_id is not None, "an awaiting study offers the 'done' control")
study_id = study_id.group(1)
tech.post(f"/radiology/worklist?handler=Done&ModalityId={xray[0]}",
          {"StudyId": study_id, "ModalityId": xray[0], "FilmSize": "14x17", "FilmCount": "1"},
          on_xray)
after = tech.get(f"/radiology/worklist?ModalityId={xray[0]}")
check(f'name="StudyId" value="{study_id}"' not in after,
      "once done, the study no longer offers 'done' (U7)")

_, again = tech.post(f"/radiology/worklist?handler=Done&ModalityId={xray[0]}",
                     {"StudyId": study_id, "ModalityId": xray[0], "FilmCount": "1"}, on_xray)
check("not waiting to be performed" in again, "a second 'done' is refused, not silently repeated")

# ---- 4. the report ----------------------------------------------------------------
print("\n4. the radiologist reports it (US10.2)")
report = radiologist.get(f"/radiology/report/{study_id}")
check("Findings" in report, "the report editor opens")
check("X-ray" in report, "and names the modality")

provisional = radiologist.get(f"/radiology/print/{study_id}")
check("PROVISIONAL" in provisional or "not signed" in provisional,
      "printing before a report exists is marked provisional")

radiologist.post(f"/radiology/report/{study_id}?handler=Save", {
    "Findings": f"Both lung fields clear. Cardiac silhouette normal. [{stamp}]",
    "Impression": "Normal chest radiograph."}, report)
report = radiologist.get(f"/radiology/report/{study_id}")
check(stamp in report, "the findings are saved")

unsigned = radiologist.get(f"/radiology/print/{study_id}")
check("PROVISIONAL" in unsigned, "an unsigned report still prints provisional (US10.2 AC)")
check("IMPRESSION" in unsigned, "with the impression on it")

# ---- 5. signing -------------------------------------------------------------------
print("\n5. signing makes it final and deliverable")
url, _ = radiologist.post(f"/radiology/report/{study_id}?handler=Sign", {}, report)
check("/radiology/print/" in url, "signing lands on the printable report")

final = radiologist.get(f"/radiology/print/{study_id}")
check("PROVISIONAL" not in final, "the signed report is no longer provisional")
check("Dr. Farhana Rahman" in final, "and carries the signing consultant")

signed_report = radiologist.get(f"/radiology/report/{study_id}")
check("already signed" in signed_report or "signed and deliverable" in signed_report,
      "the editor refuses further editing once signed")
check('name="Findings"' not in signed_report, "the editing form is absent, not disabled")

delivery = counter.get("/diagnostics/delivery")
check(name in delivery, "the order is ready for delivery in the existing M8 flow")

print()
if fail:
    print(f"RADIOLOGY THREAD FAILED — {len(fail)} check(s)")
    for f in fail:
        print(f"  - {f}")
    sys.exit(1)
print("RADIOLOGY THREAD PASSED")
