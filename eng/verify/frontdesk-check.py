#!/usr/bin/env python3
"""Spec 0018 AC 1: the help-desk estimate equals posted charges + accrued bed days − advances,
and reading it changes nothing. Dirty-database-tolerant (registers its own patient, frees the
bed afterwards); numbers rely only on seeded constant rates (bed 1200, admission fee 500)."""
import http.cookiejar, json, os, pathlib, re, sys, time, urllib.parse, urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import fixture, on_exit, release_bed   # noqa: E402

BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')


class Session:
    def __init__(self, user, password):
        self.op = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        self.post("/login", {"Username": user, "Password": password}, self.get("/login"))

    def get(self, path):
        with self.op.open(BASE + path) as r:
            return r.read().decode()

    def post(self, path, fields, page_html=None):
        html = page_html if page_html is not None else self.get(path)
        m = TOKEN_RE.search(html)
        data = dict(fields)
        if m:
            data["__RequestVerificationToken"] = m.group(1)
        req = urllib.request.Request(BASE + path, data=urllib.parse.urlencode(data, doseq=True).encode())
        with self.op.open(req) as r:
            return r.geturl(), r.read().decode()


fail = []


def check(cond, msg):
    print(f"  {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


print("FRONT DESK ESTIMATE CHECK (M2 US2.2)")
fd = Session("jashim", "Demo#1234")
op = Session("rasel", "Demo#1234")

stamp = f"{int(time.time() * 1000) % 100000:05d}"   # ms: two runs in one second differ
tag = f"Desk Check {stamp}"
# Unique phone per run, and acknowledge the duplicate guard the way an operator does —
# a previous run of this very script is a legitimate near-duplicate (edge 23).
fd.post("/registration/new", {
    "FullName": tag, "Sex": "M", "AgeOrDob": "50", "Phone": f"017{stamp}{stamp[:3]}",
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(fd.get("/api/typeahead/patients?q=" + urllib.parse.quote(tag)))
check(len(hits) >= 1, "check patient registered")   # newest first; repeats are fine
pid = str(hits[0]["value"])

admit = fd.get("/ipd/admit")
bed = fixture(re.search(r'<option value="(\d+)">GW[MF]-\d+', admit),
              "no free general-ward bed — every ward bed is occupied",
              "a thread that admits must discharge; reset the database if a run was killed")
check(bed is not None, "free general bed available")
url, _ = fd.post("/ipd/admit", {
    "PatientId": pid, "Source": "direct", "BedId": bed.group(1),
    "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
adm = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused").group(1)

# The absconded exit and the housekeeping below are the last two statements of the happy
# path; a failure on any assertion between here and there used to keep the bed (spec 0029 F3).
def _return_bed():
    fd.post(f"/ipd/folio/{adm}?handler=Absconded", {"AdmissionId": adm},
            fd.get(f"/ipd/folio/{adm}"))
    release_bed(fd, bed.group(1))


on_exit(f"admission {adm} closed and bed returned", _return_bed)

# Front desk BEFORE any folio view by money-side staff: fee 500 posted, bed day 1200 accrued.
desk = fd.get(f"/frontdesk?patientId={pid}")
net = re.search(r'Net payable now[^৳&]*(?:&#x9F3; |৳ )([\d,]+)', desk)
check(net is not None and net.group(1).replace(",", "") == "1700",
      f"estimate = fee 500 posted + bed 1200 accrued (got {net and net.group(1)})")

# Reading the desk posted nothing: the folio still has ONE line (the admission fee).
sess = op.get("/billing/session")
if "Your counter is open" not in sess:
    counter = re.search(r'value="(\d+)"[^>]*>[^<]*Front Desk', sess)
    op.post("/billing/session", {"CounterId": counter.group(1) if counter else "1",
                                 "OpeningFloat": "500"}, sess)
folio = op.get(f"/ipd/folio/{adm}")          # rasel's view catches the bed day up (0017 rule)
op.post(f"/ipd/folio/{adm}?handler=Advance",
        {"AdmissionId": adm, "AdvanceAmount": "700", "AdvanceTender": "cash"}, folio)

desk = fd.get(f"/frontdesk?patientId={pid}")
net = re.search(r'Net payable now[^৳&]*(?:&#x9F3; |৳ )([\d,]+)', desk)
check(net is not None and net.group(1).replace(",", "") == "1000",
      f"estimate = 1700 posted − 700 advance (got {net and net.group(1)})")

desk2 = fd.get(f"/frontdesk?patientId={pid}")
check(desk2.count("Net payable now") == desk.count("Net payable now")
      and re.search(r'Net payable now[^৳&]*(?:&#x9F3; |৳ )([\d,]+)', desk2).group(1) == net.group(1),
      "a second read changes nothing (read-only)")

# Cleanup: close the admission and free the bed for the next run. Registered as teardown at
# admission time too, so it happens whether or not the assertions above got this far.
_return_bed()

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("FRONT DESK CHECK PASSED")
