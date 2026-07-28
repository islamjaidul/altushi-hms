#!/usr/bin/env python3
"""Spec 0017 end-to-end thread: admit -> bed days -> advance -> nurse posting ->
medicine indent FEFO -> indoor investigation -> return -> R4 block/release (incl.
the OPD counter guard) -> transfer -> settlement (advance applied) -> discharge ->
certificate. Assertions track the records this run creates (by id/number), so the
script is dirty-database-tolerant and usable by the upgrade gate (ADR-0022)."""
import http.cookiejar, json, os, pathlib, re, sys, time, urllib.parse, urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import (fixture, on_exit, record, release_bed,   # noqa: E402
                       settle_and_discharge, tag)

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


def step(n, text):
    print(f"  {n:2}. {text}")


def check(cond, msg):
    print(f"      {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


def approve(sup, type_label):
    """Approve the pending request of the given humanized type label."""
    inbox = sup.get("/admin/approvals")
    m = re.search(type_label + r'.*?name="id" value="(\d+)"', inbox, re.S)
    check(m is not None, f"'{type_label}' request in the approvals inbox")
    if m:
        sup.post("/admin/approvals?handler=Decide", {"id": m.group(1), "approve": "true", "note": "ok"}, inbox)
    return m


print("IPD THREAD (M6 + R4)")

fd = Session("jashim", "Demo#1234")     # front desk: admissions, beds, blocks
op = Session("rasel", "Demo#1234")      # billing operator: advances, settlement
nu = Session("nasrin", "Demo#1234")     # nurse: service posting, indents
ph = Session("parvin", "Demo#1234")     # pharmacist: indent issue
sup = Session("shahid", "Demo#1234")    # supervisor: approvals

# 1 — a patient of our own -------------------------------------------------
step(1, "Front desk registers the IPD patient")
# A per-run patient: reusing one fixed name means a run that dies mid-flight leaves an open
# admission behind, and every later run is refused ("already has an open admission"). The
# duplicate guard is acknowledged the way the operator's "continue below" does (edge 23).
stamp = f"{int(time.time() * 1000) % 100000:05d}"   # ms: two runs in one second differ
NAME = tag(f"Ipd Thread {stamp}")
fd.post("/registration/new", {
    "FullName": NAME, "Sex": "F", "AgeOrDob": "32",
    "Phone": f"017555{stamp}", "PatientType": "general",
    "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(fd.get("/api/typeahead/patients?q=" + urllib.parse.quote(NAME)))
check(len(hits) > 0, "patient registered and findable")
pid = str(hits[-1]["value"])
record("patient", pid)

# 2 — admission ------------------------------------------------------------
step(2, "Admission into a free general bed (§5 M6)")
admit = fd.get("/ipd/admit")
bed = fixture(re.search(r'<option value="(\d+)">GW[MF]-\d+', admit),
              "no free general-ward bed — every ward bed is occupied",
              "a thread that admits must discharge; reset the database if a run was killed")
check(bed is not None, "a free general-ward bed is on offer")
url, html = fd.post("/ipd/admit", {
    "PatientId": pid, "Source": "direct", "ProvisionalDx": "Acute gastritis",
    "BedId": bed.group(1), "ServiceChargePct": "5", "ReserveOnly": "false"}, admit)
m = fixture(re.search(r"/ipd/folio/(\d+)", url), "admission was refused",
            "the ward may be full, or the patient may already have an open admission")
check(m is not None, "admitted — folio opened")
adm = m.group(1)
record("admission", adm)

# The bed is now this run's to return. Registered here rather than at the end, because a
# failure at any later step used to cost the ward a bed permanently (spec 0029 F3): every
# occupied bed in the local database at the start of that spec came from a thread that died
# before reaching its housekeeping step.
OURS = [bed.group(1)]
on_exit(f"admission {adm} discharged and bed(s) returned",
        lambda: settle_and_discharge(lambda u: Session(u, "Demo#1234"), adm, OURS))

# 3 — bed day + admission fee post on first money-side view ----------------
step(3, "Folio catches up bed days idempotently (P18)")
folio = op.get(f"/ipd/folio/{adm}")
check("Admission Fee" in folio, "admission fee posted at admission")
check("bed GW" in folio, "today's bed day auto-posted")   # "… — bed GWM-01, dd MMM yyyy"
before = folio.count("bed GW")
folio = op.get(f"/ipd/folio/{adm}")
check(folio.count("bed GW") == before, "second view posts nothing new")

# 4 — advance through the drawer -------------------------------------------
step(4, "Advance rides the operator's counter session")
sess = op.get("/billing/session")
if "Your counter is open" not in sess:
    counter = re.search(r'value="(\d+)"[^>]*>[^<]*Front Desk', sess)
    op.post("/billing/session", {"CounterId": counter.group(1) if counter else "1",
                                 "OpeningFloat": "1000"}, sess)
folio = op.get(f"/ipd/folio/{adm}")
op.post(f"/ipd/folio/{adm}?handler=Advance",
        {"AdmissionId": adm, "AdvanceAmount": "5000", "AdvanceTender": "cash"}, folio)
folio = op.get(f"/ipd/folio/{adm}")
check("5,000" in folio or "5000" in folio, "advance held on the folio")

# 5 — nurse posts a ward service (US6.1) -----------------------------------
step(5, "Nurse posts oxygen — price from the rate plan, her name on the line")
folio = nu.get(f"/ipd/folio/{adm}")
oxy = fixture(re.search(r'<option value="(\d+)">Oxygen[^<]*</option>', folio),
              "no Oxygen service in the ward catalog")
check(oxy is not None, "oxygen service offered from the catalog")
nu.post(f"/ipd/folio/{adm}?handler=Service",
        {"AdmissionId": adm, "ServiceId": oxy.group(1), "Qty": "2"}, folio)
folio = nu.get(f"/ipd/folio/{adm}")
check("Oxygen" in folio and "Nasrin" in folio, "posting appears with the nurse's identity")

# 6 — medicine indent -> pharmacy FEFO issue (5A-9) ------------------------
step(6, "Ward indent issues FEFO at batch MRP to the folio")
napa = fixture(re.search(r'<option value="(\d+)">Napa', folio),
               "no medicine offered on the ward indent form")
check(napa is not None, "medicine on the indent form")
# On a database with history the queue holds other wards' indents too — issue OUR one,
# identified as the id that appeared after we raised it (not whatever is at the top).
before = set(re.findall(r'name="IndentId" value="(\d+)"', ph.get("/pharmacy/indents")))
nu.post(f"/ipd/folio/{adm}?handler=Indent", {
    "AdmissionId": adm, "ProductIds": [napa.group(1), "0", "0"],
    "ItemQtys": ["4", "0", "0"], "IndentNote": "post-op"}, folio)
queue = ph.get("/pharmacy/indents")
mine = sorted(set(re.findall(r'name="IndentId" value="(\d+)"', queue)) - before, key=int)
iid = fixture(re.match(r"(\d+)", mine[-1]) if mine else None,
              "the ward indent never reached the pharmacy issue queue")
check(iid is not None, "indent waits in the pharmacy issue queue")
outlet = fixture(re.search(r'<option value="(\d+)">(?!outlet)', queue),
                 "no pharmacy outlet available to issue from")
ph.post("/pharmacy/indents?handler=Issue",
        {"IndentId": iid.group(1), "OutletId": outlet.group(1)}, queue)
folio = nu.get(f"/ipd/folio/{adm}")
check("batch" in folio and "Napa" in folio, "folio line carries the issuing batch")

# 7 — indoor investigation (§11 indoor branch) -----------------------------
step(7, "Investigation indent: charge on folio, sample raised, no invoice gate")
cbc = fixture(re.search(r'name="TestIds" value="(\d+)"', folio),
              "no test offered on the indoor investigation form")
check(cbc is not None, "test catalog offered")
nu.post(f"/ipd/folio/{adm}?handler=OrderTests",
        {"AdmissionId": adm, "TestIds": [cbc.group(1)]}, folio)
folio = nu.get(f"/ipd/folio/{adm}")
check("Diagnostics" in folio, "test charge is on the folio")
board = Session("ripon", "Demo#1234").get("/lis/board")
check(NAME in board, "sample on the LIS board without payment (indoor rule)")

# 8 — discharge-time return restocks (0016 deferral #11) -------------------
step(8, "Partial return of the indent restocks its exact batch")
nu.post(f"/ipd/folio/{adm}?handler=ReturnItem", {
    "AdmissionId": adm, "IndentId": iid.group(1),
    "ReturnProductId": napa.group(1), "ReturnQty": "1"}, folio)
folio = nu.get(f"/ipd/folio/{adm}")
check("Return:" in folio, "negative folio line reverses the charged MRP")

# 9 — R4 block ⇄ release, including the outdoor counter guard --------------
step(9, "R4: block bars service and OPD; release restores")
fd.post("/ipd/admissions?handler=RaiseBlock",
        {"AdmissionId": adm, "Reason": "large unpaid due"}, fd.get("/ipd/admissions"))
approve(sup, "Patient-block")
adm_page = fd.get("/ipd/admissions")
m = re.search(r'name="AdmissionId" value="' + adm + r'"\s*/>\s*<input type="hidden" name="ApprovalId" value="(\d+)"', adm_page)
check(m is not None, "approved block ready to apply")
fd.post("/ipd/admissions?handler=ApplyBlock",
        {"AdmissionId": adm, "ApprovalId": m.group(1)}, adm_page)
folio = nu.get(f"/ipd/folio/{adm}")
check("Blocked for dues" in folio, "folio shows the R4 hold")
_, html = nu.post(f"/ipd/folio/{adm}?handler=Service",
                  {"AdmissionId": adm, "ServiceId": oxy.group(1), "Qty": "1"}, folio)
check("blocked" in html.lower(), "service posting refused while blocked")
opd = op.get("/billing/opd")
svc = fixture(re.search(r'name="catalogId" value="(\d+)"', opd),
              "no service on the OPD counter catalog")
opd = op.post("/billing/opd?handler=Add", {"catalogId": svc.group(1), "PatientId": pid}, opd)[1]
_, html = op.post("/billing/opd?handler=Save",
                  {"PatientId": pid, "Items": [svc.group(1)], "PaidNow": "0", "Tender": "cash"}, opd)
check("blocked" in html.lower(), "OPD counter refuses the blocked patient (R4 outdoor guard)")
fd.post("/ipd/admissions?handler=RaiseRelease",
        {"AdmissionId": adm, "Reason": "family paid at counter"},
        fd.get("/ipd/admissions?Tab=blocked"))
approve(sup, "Patient-release")
blocked_page = fd.get("/ipd/admissions?Tab=blocked")
m = re.search(r'name="AdmissionId" value="' + adm + r'"\s*/>\s*<input type="hidden" name="ApprovalId" value="(\d+)"', blocked_page)
check(m is not None, "approved release ready to apply")
fd.post("/ipd/admissions?handler=ApplyRelease",
        {"AdmissionId": adm, "ApprovalId": m.group(1)}, blocked_page)
folio = nu.get(f"/ipd/folio/{adm}")
check("Blocked for dues" not in folio, "released — folio open again")

# 10 — transfer (US6.3) ----------------------------------------------------
step(10, "Transfer to another bed — history keeps the moment")
folio = fd.get(f"/ipd/folio/{adm}")
# Any free bed, preferring a different class. The assertion is that the move is recorded with
# its moment in the bed history; insisting on a *cabin* only meant one thread monopolised the
# scarcest class there is — three of them — and failed the moment another run held one.
targets = [(bid, code) for bid, code in
           re.findall(r'<option value="(\d+)">([A-Z]+-\d+)', folio) if bid != bed.group(1)]
targets.sort(key=lambda t: (t[1].startswith("GW"), t[1]))   # non-general first
cab = fixture(targets[0] if targets else None,
              "no second free bed to transfer into",
              "the transfer test needs one free bed besides the one it admitted into")
to_bed, to_code = cab
fd.post(f"/ipd/folio/{adm}?handler=Transfer",
        {"AdmissionId": adm, "ToBedId": to_bed}, folio)
OURS.append(to_bed)
folio = fd.get(f"/ipd/folio/{adm}")
check(to_code in folio, f"the {to_code} stay is open in the bed history")

# 11 — discharge & settlement (US6.2) --------------------------------------
step(11, "Discharge: summary → clearance → draft → invoice with advance applied")
dis = fd.get(f"/ipd/discharge/{adm}")
fd.post(f"/ipd/discharge/{adm}?handler=Initiate",
        {"AdmissionId": adm, "ClinicalSummary": "Recovered. Advice: rest, antacids."}, dis)
dis = fd.get(f"/ipd/discharge/{adm}")
fd.post(f"/ipd/discharge/{adm}?handler=Clear", {"AdmissionId": adm}, dis)
dis = op.get(f"/ipd/discharge/{adm}")
op.post(f"/ipd/discharge/{adm}?handler=Prepare", {"AdmissionId": adm}, dis)
dis = op.get(f"/ipd/discharge/{adm}")
check("Settlement draft" in dis, "draft assembled from the folio, zero re-entry")
check("Service charge 5%" in dis, "service-charge % line posted at draft")
url, html = op.post(f"/ipd/discharge/{adm}?handler=Confirm",
                    {"AdmissionId": adm, "DiscountFlat": "0"}, dis)
check("/billing/invoice/" in url, "settlement invoice created")
inv_id = url.rstrip("/").split("/")[-1]
record("invoice", inv_id)
invoice = op.get(f"/billing/invoice/{inv_id}")
check("INV-" in invoice, "invoice printable")
dues = op.get("/billing/dues?Q=" + urllib.parse.quote(NAME))
if f'name="InvoiceId" value="{inv_id}"' in dues:
    amt = re.search(r'Due[^0-9]*([\d,]+)', dues)
    op.post("/billing/dues?handler=Collect",
            {"InvoiceId": inv_id, "Amount": amt.group(1).replace(",", ""), "Tender": "cash"}, dues)
    dues = op.get("/billing/dues?Q=" + urllib.parse.quote(NAME))
    check(f'name="InvoiceId" value="{inv_id}"' not in dues, "remaining balance collected")
dis = op.get(f"/ipd/discharge/{adm}")
op.post(f"/ipd/discharge/{adm}?handler=Discharge", {"AdmissionId": adm}, dis)
dis = op.get(f"/ipd/discharge/{adm}")
check("Gate pass" in dis, "gate pass printed — bed off to cleaning")

# 12 — certificate ---------------------------------------------------------
step(12, "Discharge certificate: sequential number, reprint counted")
certs = op.get("/ipd/certificates")
op.post("/ipd/certificates?handler=Issue",
        {"AdmissionId": adm, "Kind": "discharge", "Extra": ""}, certs)
certs = op.get("/ipd/certificates")
cert_no = re.search(r"(DIS-[\d-]+)", certs)
check(cert_no is not None, "certificate issued with a DIS- number")
cid = fixture(re.search(r'name="CertificateId" value="(\d+)"', certs),
              "the certificate was not issued, so there is nothing to reprint")
op.post("/ipd/certificates?handler=Reprint", {"CertificateId": cid.group(1)}, certs)
certs = op.get("/ipd/certificates")
check(cert_no.group(1) in certs, "reprint kept the same number")

# 13 — reports & board ------------------------------------------------------
step(13, "The day shows on the IPD report and ward board")
report = fd.get("/ipd/reports")
check(NAME in report, "admission and discharge on the report")
board = fd.get("/ipd/board")
check("Cleaning" in board or "cleaning" in board, "the vacated bed is in cleaning")

# 14 — housekeeping so the ward survives repeated runs -----------------------
# Discharge and transfer both leave a bed in Cleaning (§11). Real housekeeping clears it;
# here the script does, or a ward of 8 beds would be exhausted after 8 runs and every later
# run — including the upgrade gate's — would fail for want of a bed.
step(14, "Housekeeping: hand the beds back to the ward")
for bed_id in set(OURS):
    release_bed(fd, bed_id)
board = fd.get("/ipd/board")
check(board.count("Free") >= 1, "at least one bed is free again")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("IPD THREAD PASSED — all checks green")
