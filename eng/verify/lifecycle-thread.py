#!/usr/bin/env python3
"""Spec 0020: one patient's whole life across every built module, in one run — the test that
found the gaps this spec fixes. Unlike the per-module threads, this one exists to catch the
SEAMS: what one module writes, another must see. Dirty-database tolerant (its patient is its
own, asserted by id), so the upgrade gate runs it too."""
import json, re, sys, time, urllib.parse, http.cookiejar, urllib.request

BASE = "http://localhost:5199"
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


def open_counter(sess, kind="Front Desk", float_amt="2000"):
    page = sess.get("/billing/session")
    if "Your counter is open" in page:
        return
    m = re.search(r'value="(\d+)"[^>]*>[^<]*' + kind, page)
    sess.post("/billing/session",
              {"CounterId": m.group(1) if m else "1", "OpeningFloat": float_amt}, page)


print("PATIENT LIFECYCLE THREAD (cross-module seams, spec 0020)")

fd = Session("jashim", "Demo#1234")     # front desk
op = Session("rasel", "Demo#1234")      # billing
lab = Session("ripon", "Demo#1234")     # lab
ph = Session("parvin", "Demo#1234")     # pharmacy
nu = Session("nasrin", "Demo#1234")     # nurse
sup = Session("shahid", "Demo#1234")    # supervisor
md = Session("md", "Demo#1234")         # MD

stamp = int(time.time() * 1000) % 100000   # ms: two runs in one second differ
name = f"Lifecycle {stamp}"
phone_digits = f"0171{stamp:05d}00"[:11]
phone_typed = phone_digits                      # exactly what an operator types

# 1 — registration ---------------------------------------------------------
step(1, "Registration: the patient exists and is findable the way people search")
# A previous run of this script is a legitimate near-duplicate (same name shape, same age),
# so the guard is acknowledged exactly as the operator's "continue below" does (edge 23).
fd.post("/registration/new", {
    "FullName": name, "Sex": "M", "AgeOrDob": "45", "Phone": phone_typed,
    "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
hits = json.loads(fd.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
check(len(hits) > 0, "found by name")
pid = str(hits[0]["value"])
by_digits = json.loads(fd.get(f"/api/typeahead/patients?q={phone_digits}"))
check(any(str(h["value"]) == pid for h in by_digits),
      "found by the phone typed as plain digits (spec 0020 gap 1)")
by_tail = json.loads(fd.get(f"/api/typeahead/patients?q={phone_digits[-6:]}"))
check(any(str(h["value"]) == pid for h in by_tail), "found by the tail of the number")
check(name in fd.get(f"/registration?Q={phone_digits}"), "directory search accepts digits too")

# 2 — OPD visit ------------------------------------------------------------
step(2, "OPD consultation: bill, pay, and the money spine records it")
open_counter(op)
opd = op.get(f"/billing/opd?PatientId={pid}")
svc = re.search(r'name="catalogId" value="(\d+)"', opd)
opd = op.post("/billing/opd?handler=Add", {"catalogId": svc.group(1), "PatientId": pid}, opd)[1]
gross = re.search(r'data-gross="(\d+)"', opd)
url, _ = op.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [svc.group(1)],
    "PaidNow": gross.group(1) if gross else "0", "Tender": "cash"}, opd)
check("/billing/invoice/" in url, "OPD invoice created and paid in full")

# 3 — diagnostics → LIS ----------------------------------------------------
step(3, "Investigation: payment releases the lab, the sample reaches the board")
order = op.get(f"/diagnostics/order?PatientId={pid}")
m = re.search(r'CBC.{0,800}?name="catalogId" value="(\d+)"', order, re.S) \
    or re.search(r'name="catalogId" value="(\d+)".{0,400}?CBC', order, re.S)
check(m is not None, "CBC on the order form")
order = op.post("/diagnostics/order?handler=Add", {"catalogId": m.group(1), "PatientId": pid}, order)[1]
g2 = re.search(r'data-gross="(\d+)"', order)
url, _ = op.post("/diagnostics/order?handler=Save", {
    "PatientId": pid, "Items": [m.group(1)], "DiscountFlat": "0",
    "PaidNow": g2.group(1) if g2 else "400", "Tender": "cash"}, order)
check("/diagnostics/order/" in url, "test order created and paid")
check(name in lab.get("/lis/board"), "paid order released to the lab (§9A.2 seam)")

# 4 — pharmacy, outdoor ----------------------------------------------------
step(4, "Pharmacy: a credit sale becomes a due the billing counter can see")
open_counter(ph, "Pharmacy", "500")
pos = ph.get(f"/pharmacy/pos?PatientId={pid}")
prod = re.search(r'name="productId" value="(\d+)"', pos)
pos = ph.post("/pharmacy/pos?handler=Add", {"productId": prod.group(1), "PatientId": pid}, pos)[1]
url, _ = ph.post("/pharmacy/pos?handler=Save", {
    "PatientId": pid, "Items": [prod.group(1)], "Qtys": ["2"],
    "PaidNow": "0", "Tender": "cash"}, pos)
check("/billing/invoice/" in url, "pharmacy credit sale saved")
pharm_inv = url.rstrip("/").split("/")[-1]
dues = op.get("/billing/dues?Q=" + urllib.parse.quote(name))
check(f'name="InvoiceId" value="{pharm_inv}"' in dues,
      "the pharmacy due is visible at the billing counter (shared spine)")

# 5 — admission ------------------------------------------------------------
step(5, "Admission: a bed, a folio, and the outdoor screens now warn")
admit = fd.get("/ipd/admit")
bed = re.search(r'<option value="(\d+)">GW[MF]-\d+', admit)
url, _ = fd.post("/ipd/admit", {
    "PatientId": pid, "Source": "opd", "ProvisionalDx": "lifecycle",
    "BedId": bed.group(1), "ServiceChargePct": "0", "ReserveOnly": "false"}, admit)
mm = re.search(r"/ipd/folio/(\d+)", url)
check(mm is not None, "admitted, folio open")
adm = mm.group(1)
pos = ph.get(f"/pharmacy/pos?PatientId={pid}")
check("is <b>admitted</b>" in pos or "admitted" in pos.lower(),
      "pharmacy POS warns the patient is admitted (spec 0020 gap 3)")
opd = op.get(f"/billing/opd?PatientId={pid}")
check("admitted" in opd.lower(), "OPD billing warns the patient is admitted")

# 6 — ward work ------------------------------------------------------------
step(6, "Ward: nurse posts a service, indent issues FEFO to the folio")
folio = nu.get(f"/ipd/folio/{adm}")
oxy = re.search(r'<option value="(\d+)">Oxygen[^<]*</option>', folio)
nu.post(f"/ipd/folio/{adm}?handler=Service",
        {"AdmissionId": adm, "ServiceId": oxy.group(1), "Qty": "2"}, folio)
folio = nu.get(f"/ipd/folio/{adm}")
check("Oxygen" in folio and "Nasrin" in folio, "ward service posted with the nurse's name")
napa = re.search(r'<option value="(\d+)">Napa', folio)
nu.post(f"/ipd/folio/{adm}?handler=Indent", {
    "AdmissionId": adm, "ProductIds": [napa.group(1), "0", "0"],
    "ItemQtys": ["3", "0", "0"], "IndentNote": "lifecycle"}, folio)
queue = ph.get("/pharmacy/indents")
iid = re.search(r'name="IndentId" value="(\d+)"', queue)
outlet = re.search(r'<option value="(\d+)">(?!outlet)', queue)
ph.post("/pharmacy/indents?handler=Issue",
        {"IndentId": iid.group(1), "OutletId": outlet.group(1)}, queue)
folio = nu.get(f"/ipd/folio/{adm}")
check("batch" in folio, "indent issued onto the folio at batch MRP")

# 7 — discharge with money owed -------------------------------------------
step(7, "Discharge: the gate refuses to open silently on an unpaid bill")
dis = fd.get(f"/ipd/discharge/{adm}")
fd.post(f"/ipd/discharge/{adm}?handler=Initiate",
        {"AdmissionId": adm, "ClinicalSummary": "recovered"}, dis)
dis = fd.get(f"/ipd/discharge/{adm}")
fd.post(f"/ipd/discharge/{adm}?handler=Clear", {"AdmissionId": adm}, dis)
dis = op.get(f"/ipd/discharge/{adm}")
op.post(f"/ipd/discharge/{adm}?handler=Prepare", {"AdmissionId": adm}, dis)
dis = op.get(f"/ipd/discharge/{adm}")
url, _ = op.post(f"/ipd/discharge/{adm}?handler=Confirm",
                 {"AdmissionId": adm, "DiscountFlat": "0"}, dis)
check("/billing/invoice/" in url, "settlement invoice created")
settle_inv = url.rstrip("/").split("/")[-1]
dis = op.get(f"/ipd/discharge/{adm}")
check("is still owed" in dis, "discharge shows the whole outstanding position")
check(pharm_inv in dis or "other counter" in dis,
      "the outdoor pharmacy due is listed at the gate, not just this admission's bill")
_, refused = op.post(f"/ipd/discharge/{adm}?handler=Discharge",
                     {"AdmissionId": adm, "OutstandingReason": ""}, dis)
check("still owes" in refused, "a reasonless discharge is refused server-side (gap 2)")
check("Gate pass" not in op.get(f"/ipd/discharge/{adm}"), "no gate pass was printed")

# 8 — collect, then leave clean -------------------------------------------
step(8, "Collect everything, then the gate opens with one click")
for inv in (settle_inv, pharm_inv):
    page = op.get(f"/billing/dues?Q=" + urllib.parse.quote(name))
    row = re.search(r'name="InvoiceId" value="' + inv + r'".{0,600}?name="Amount"[^>]*value="(\d+)"',
                    page, re.S)
    amount = row.group(1) if row else None
    if amount is None:
        m2 = re.search(r'name="InvoiceId" value="' + inv + r'".{0,800}?(?:&#x9F3; |৳ )([\d,]+)',
                       page, re.S)
        amount = m2.group(1).replace(",", "") if m2 else None
    if amount:
        op.post("/billing/dues?handler=Collect",
                {"InvoiceId": inv, "Amount": amount, "Tender": "cash"}, page)
dis = op.get(f"/ipd/discharge/{adm}")
check("is still owed" not in dis, "nothing outstanding once collected")
op.post(f"/ipd/discharge/{adm}?handler=Discharge", {"AdmissionId": adm}, dis)
check("Gate pass" in op.get(f"/ipd/discharge/{adm}"), "gate pass printed on a clean account")

# 9 — the record afterwards ------------------------------------------------
step(9, "Afterwards: certificate, history, and a return visit still work")
certs = op.get("/ipd/certificates")
op.post("/ipd/certificates?handler=Issue",
        {"AdmissionId": adm, "Kind": "discharge", "Extra": ""}, certs)
check("DIS-" in op.get("/ipd/certificates"), "discharge certificate issued")
desk = fd.get(f"/frontdesk?patientId={pid}")
check("IPD" in desk, "the admission shows in the help-desk history")
opd = op.get(f"/billing/opd?PatientId={pid}")
check("admitted" not in opd.lower(), "the admitted warning is gone once discharged")
svc2 = re.search(r'name="catalogId" value="(\d+)"', opd)
opd = op.post("/billing/opd?handler=Add", {"catalogId": svc2.group(1), "PatientId": pid}, opd)[1]
g3 = re.search(r'data-gross="(\d+)"', opd)
url, _ = op.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [svc2.group(1)],
    "PaidNow": g3.group(1) if g3 else "0", "Tender": "cash"}, opd)
check("/billing/invoice/" in url, "the patient can return as an outpatient")

# 10 — housekeeping --------------------------------------------------------
# The discharge left the bed in Cleaning (§11); hand it back so repeated runs (and the
# upgrade gate) never exhaust the ward.
step(10, "Housekeeping: the vacated bed returns to the ward")
board = fd.get("/ipd/board")
fd.post("/ipd/board?handler=CleaningDone", {"BedId": bed.group(1)}, board)
check("Free" in fd.get("/ipd/board"), "bed free again for the next patient")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("LIFECYCLE THREAD PASSED — the seams hold end to end")
