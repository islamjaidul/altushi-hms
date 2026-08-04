#!/usr/bin/env python3
"""Advanced edge cases of the patient lifecycle (specs 0020/0021) — the paths a patient takes
when they do NOT simply get better and go home: dying, absconding, being blocked at the wrong
moment, being double-clicked, asking for more stock than exists.

Every scenario provisions its own patient and hands its bed back, so the script is
dirty-database tolerant and safe to run repeatedly (including in the upgrade gate)."""
import http.cookiejar, json, os, pathlib, re, sys, time, urllib.parse, urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import fixture, record, tag   # noqa: E402

BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
SUBMIT_RE = re.compile(r'name="SubmissionToken" value="([0-9a-fA-F-]{36})"')
ERR_RE = re.compile(r'class="alert bad".*?<span>(.*?)</span>', re.S)


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


def case(n, text):
    print(f"  {n:2}. {text}")


def check(cond, msg):
    print(f"      {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


def error_on(html):
    m = ERR_RE.search(html)
    return re.sub(r"<[^>]+>", "", m.group(1)).strip() if m else ""


print("ADVANCED EDGE CASES (specs 0020 / 0021)")

fd = Session("jashim", "Demo#1234")     # front desk
op = Session("rasel", "Demo#1234")      # billing
nu = Session("nasrin", "Demo#1234")     # nurse
ph = Session("parvin", "Demo#1234")     # pharmacy
sup = Session("shahid", "Demo#1234")    # supervisor


def open_counter(sess, kind="Front Desk", amount="5000"):
    page = sess.get("/billing/session")
    if "Your counter is open" in page:
        return
    m = re.search(r'value="(\d+)"[^>]*>[^<]*' + kind, page)
    sess.post("/billing/session",
              {"CounterId": m.group(1) if m else "1", "OpeningFloat": amount}, page)


def new_patient(prefix, age="50"):
    stamp = f"{int(time.time() * 1000) % 100000:05d}"
    name = tag(f"{prefix} {stamp}")
    fd.post("/registration/new", {
        "FullName": name, "Sex": "M", "AgeOrDob": age, "Phone": f"012{stamp}{stamp[:3]}",
        "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
    hits = json.loads(fd.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
    return name, str(hits[0]["value"])


def admit(pid):
    page = fd.get("/ipd/admit")
    # Returning (None, None) here meant the next line asked for /ipd/folio/None and the run
    # died on a bare `HTTPError: 404` — a full ward reported as a routing bug (spec 0029 F4).
    bed = fixture(re.search(r'<option value="(\d+)">GW[MF]-\d+', page),
                  "no free general-ward bed — every ward bed is occupied",
                  "a thread that admits must discharge; reset the database if a run was killed")
    url, _ = fd.post("/ipd/admit", {
        "PatientId": pid, "Source": "direct", "BedId": bed.group(1),
        "ServiceChargePct": "0", "ReserveOnly": "false"}, page)
    m = fixture(re.search(r"/ipd/folio/(\d+)", url), "the admission was refused")
    return m.group(1), bed.group(1)


def free_bed(bed_id):
    board = fd.get("/ipd/board")
    fd.post("/ipd/board?handler=CleaningDone", {"BedId": bed_id}, board)


def ensure_ipd_counter(sess, float_amt="1000"):
    # Spec 0048: settlement needs the IPD drawer open; re-posting open for an already-open
    # counter fails server-side, which is fine (sessions are unique per counter).
    page = sess.get("/billing/session")
    if "IPD counter open" in page:
        return
    m = re.search(r'value="(\d+)"[^>]*>[^<]*IPD', page)
    if m:
        sess.post("/billing/session", {"CounterId": m.group(1), "OpeningFloat": float_amt}, page)


def close_bill(admission_id):
    """Prepare + confirm a settlement, carrying the submission token as the browser does."""
    ensure_ipd_counter(op)                # spec 0048: settlement rides the IPD drawer
    page = op.get(f"/ipd/discharge/{admission_id}")
    op.post(f"/ipd/discharge/{admission_id}?handler=Prepare", {"AdmissionId": admission_id}, page)
    page = op.get(f"/ipd/discharge/{admission_id}")
    token = SUBMIT_RE.search(page)
    url, html = op.post(f"/ipd/discharge/{admission_id}?handler=Confirm", {
        "AdmissionId": admission_id, "DiscountFlat": "0",
        "SubmissionToken": token.group(1) if token else ""}, page)
    return url, html


def collect_everything(name):
    """Collect every outstanding due for a patient, using the balance the form itself offers."""
    for _ in range(8):
        page = op.get("/billing/dues?Q=" + urllib.parse.quote(name))
        m = re.search(r'name="InvoiceId" value="(\d+)"[\s\S]{0,400}?name="Amount" value="(\d+)"', page)
        if not m:
            return True
        op.post("/billing/dues?handler=Collect",
                {"InvoiceId": m.group(1), "Amount": m.group(2), "Tender": "cash"}, page)
    return False


open_counter(op)
open_counter(ph, "Pharmacy", "1000")

# 1 — death ----------------------------------------------------------------
case(1, "A patient DIES with charges on the folio — the family can still be billed")
name, pid = new_patient("Edge Death")
adm, bed = admit(pid)
op.get(f"/ipd/folio/{adm}")                                     # bed day catch-up
folio = nu.get(f"/ipd/folio/{adm}")
oxygen = fixture(re.search(r'<option value="(\d+)">Oxygen[^<]*</option>', folio),
                 "no Oxygen service in the ward catalog")
nu.post(f"/ipd/folio/{adm}?handler=Service",
        {"AdmissionId": adm, "ServiceId": oxygen.group(1), "Qty": "3"}, folio)
fd.post(f"/ipd/folio/{adm}?handler=Death", {"AdmissionId": adm}, fd.get(f"/ipd/folio/{adm}"))
screen = op.get(f"/ipd/discharge/{adm}")
check("Close the bill" in screen, "the screen offers to close the bill, not to 'discharge'")
check("Discharge — print gate pass" not in screen, "no gate pass is offered for someone who died")
url, _ = close_bill(adm)
check("/billing/invoice/" in url, "the bill closed into an invoice (charges are not stranded)")
dues = op.get("/billing/dues?Q=" + urllib.parse.quote(name))
check('name="InvoiceId"' in dues, "the balance is collectable on the dues screen")
check("Death" in fd.get("/ipd/admissions?Tab=closed"),
      "the admission stays Death — the money step never claims a discharge")
certs = op.get("/ipd/certificates")
op.post("/ipd/certificates?handler=Issue",
        {"AdmissionId": adm, "Kind": "death", "Extra": ""}, certs)
check("DTH-" in op.get("/ipd/certificates"), "the death certificate still issues")
free_bed(bed)

# 2 — absconded ------------------------------------------------------------
case(2, "A patient ABSCONDS — §11's 'due follow-up' has an actual due to follow")
name, pid = new_patient("Edge Abscond")
adm, bed = admit(pid)
op.get(f"/ipd/folio/{adm}")
fd.post(f"/ipd/folio/{adm}?handler=Absconded", {"AdmissionId": adm}, fd.get(f"/ipd/folio/{adm}"))
url, _ = close_bill(adm)
check("/billing/invoice/" in url, "the absconded patient's bill closed into an invoice")
invoice = url.rstrip("/").split("/")[-1]
dues = op.get("/billing/dues?Q=" + urllib.parse.quote(name))
check(f'name="InvoiceId" value="{invoice}"' in dues, "the due is on the collection screen")
check("Absconded" in fd.get("/ipd/admissions?Tab=closed"), "the admission stays Absconded")
free_bed(bed)

# 3 — double submit --------------------------------------------------------
case(3, "An operator double-clicks Save — the patient is billed ONCE")
name, pid = new_patient("Edge Double")
page = op.get(f"/billing/opd?PatientId={pid}")
token = SUBMIT_RE.search(page)
check(token is not None, "the bill form carries a submission token")
catalog = re.search(r'name="catalogId" value="(\d+)"', page)
page = op.post("/billing/opd?handler=Add",
               {"catalogId": catalog.group(1), "PatientId": pid,
                "SubmissionToken": token.group(1)}, page)[1]
gross = re.search(r'data-gross="(\d+)"', page)
fields = {"PatientId": pid, "Items": [catalog.group(1)], "SubmissionToken": token.group(1),
          "PaidNow": gross.group(1) if gross else "0", "Tender": "cash"}
first, _ = op.post("/billing/opd?handler=Save", dict(fields), page)
second, _ = op.post("/billing/opd?handler=Save", dict(fields), page)
check("/billing/invoice/" in first, "the bill saved")
check(first == second, f"the second click lands on the same invoice ({first.rstrip('/').split('/')[-1]})")

# 4 — over-issue -----------------------------------------------------------
case(4, "A ward indent asks for more than the shelf holds")
name, pid = new_patient("Edge Short")
adm, bed = admit(pid)
folio = nu.get(f"/ipd/folio/{adm}")
napa = re.search(r'<option value="(\d+)">Napa', folio)
# The issue queue holds every ward's pending indents, and on a deployment there are always
# some — thirteen were waiting on hms.specshipper.com. Taking whatever is at the top issued
# somebody else's small indent, which succeeded, so the over-issue this case exists to prove
# was never attempted and the run reported a product failure. Identify OUR indent as the id
# that appeared after we raised it — the guard `ipd-thread.py` step 6 already carries.
before = set(re.findall(r'name="IndentId" value="(\d+)"', ph.get("/pharmacy/indents")))
# 9,000: more than any seeded batch holds, but inside the 1..9,999 entry bound spec 0039's
# input tier now enforces — the point of this case is the ISSUE-time stock guard, and an ask
# the request tier already refuses would never reach it.
nu.post(f"/ipd/folio/{adm}?handler=Indent", {
    "AdmissionId": adm, "ProductIds": [napa.group(1), "0", "0"],
    "ItemQtys": ["9000", "0", "0"]}, folio)
queue = ph.get("/pharmacy/indents")
mine = sorted(set(re.findall(r'name="IndentId" value="(\d+)"', queue)) - before, key=int)
indent = fixture(re.match(r"(\d+)", mine[-1]) if mine else None,
                 "the over-issue indent never reached the pharmacy queue")
outlet = fixture(re.search(r'<option value="(\d+)">(?!outlet)', queue),
                 "no pharmacy outlet available to issue from")
_, out = ph.post("/pharmacy/indents?handler=Issue",
                 {"IndentId": indent.group(1), "OutletId": outlet.group(1)}, queue)
check("sellable" in error_on(out).lower() or "sellable" in error_on(ph.get("/pharmacy/indents")).lower(),
      "the over-issue is refused with the real remaining quantity")
check(f'name="IndentId" value="{indent.group(1)}"' in ph.get("/pharmacy/indents"),
      "the indent survives the refusal and can still be issued")
fd.post(f"/ipd/folio/{adm}?handler=Absconded", {"AdmissionId": adm}, fd.get(f"/ipd/folio/{adm}"))
free_bed(bed)

# 5 — blocked at settlement ------------------------------------------------
case(5, "R4: a block FREEZES the folio; release unfreezes it")
name, pid = new_patient("Edge Block")
adm, bed = admit(pid)
op.get(f"/ipd/folio/{adm}")
for handler, extra in (("Initiate", {"ClinicalSummary": "edge"}), ("Clear", {})):
    page = op.get(f"/ipd/discharge/{adm}")
    op.post(f"/ipd/discharge/{adm}?handler={handler}", {"AdmissionId": adm, **extra}, page)


def block(admission_id, reason):
    page = fd.get("/ipd/admissions")
    fd.post("/ipd/admissions?handler=RaiseBlock",
            {"AdmissionId": admission_id, "Reason": reason}, page)
    inbox = sup.get("/admin/approvals")
    rid = re.search(r'Patient-block.*?name="id" value="(\d+)"', inbox, re.S)
    sup.post("/admin/approvals?handler=Decide",
             {"id": rid.group(1), "approve": "true", "note": "ok"}, inbox)
    page = fd.get("/ipd/admissions")
    ok = re.search(r'name="AdmissionId" value="' + admission_id
                   + r'"\s*/>\s*<input type="hidden" name="ApprovalId" value="(\d+)"', page)
    fd.post("/ipd/admissions?handler=ApplyBlock",
            {"AdmissionId": admission_id, "ApprovalId": ok.group(1)}, page)


def release(admission_id, reason):
    page = fd.get("/ipd/admissions?Tab=blocked")
    fd.post("/ipd/admissions?handler=RaiseRelease",
            {"AdmissionId": admission_id, "Reason": reason}, page)
    inbox = sup.get("/admin/approvals")
    rid = re.search(r'Patient-release.*?name="id" value="(\d+)"', inbox, re.S)
    sup.post("/admin/approvals?handler=Decide",
             {"id": rid.group(1), "approve": "true", "note": "ok"}, inbox)
    page = fd.get("/ipd/admissions?Tab=blocked")
    ok = re.search(r'name="AdmissionId" value="' + admission_id
                   + r'"\s*/>\s*<input type="hidden" name="ApprovalId" value="(\d+)"', page)
    fd.post("/ipd/admissions?handler=ApplyRelease",
            {"AdmissionId": admission_id, "ApprovalId": ok.group(1)}, page)


block(adm, "edge probe")
page = op.get(f"/ipd/discharge/{adm}")
_, frozen = op.post(f"/ipd/discharge/{adm}?handler=Prepare", {"AdmissionId": adm}, page)
message = error_on(frozen) or error_on(op.get(f"/ipd/discharge/{adm}"))
check("blocked" in message.lower() or "release" in message.lower(),
      f"a blocked folio refuses settlement, comprehensibly: {message[:70]}")
release(adm, "family paid the outdoor dues")
url, _ = close_bill(adm)
check("/billing/invoice/" in url, "after release the bill settles normally")
check(collect_everything(name), "the balance collects in full")
page = op.get(f"/ipd/discharge/{adm}")
op.post(f"/ipd/discharge/{adm}?handler=Discharge", {"AdmissionId": adm}, page)
check("Gate pass" in op.get(f"/ipd/discharge/{adm}"), "and the gate opens")
free_bed(bed)

# 5b — the dead end spec 0021 closed ---------------------------------------
case(6, "A folio SETTLED WHILE BLOCKED is not a life sentence in the ward (0021)")
name, pid = new_patient("Edge Deadend")
adm, bed = admit(pid)
op.get(f"/ipd/folio/{adm}")
for handler, extra in (("Initiate", {"ClinicalSummary": "edge"}), ("Clear", {}), ("Prepare", {})):
    page = op.get(f"/ipd/discharge/{adm}")
    op.post(f"/ipd/discharge/{adm}?handler={handler}", {"AdmissionId": adm, **extra}, page)
block(adm, "due found mid-settlement")          # the block lands between prepare and confirm
page = op.get(f"/ipd/discharge/{adm}")
token = SUBMIT_RE.search(page)
# Post the token only when the page rendered one. The blocked render hides the confirm form,
# and posting SubmissionToken="" is not what any browser does — since spec 0039 the input
# gate refuses an unparseable posted value instead of silently binding Guid.Empty.
confirm_fields = {"AdmissionId": adm, "DiscountFlat": "0"}
if token:
    confirm_fields["SubmissionToken"] = token.group(1)
ensure_ipd_counter(op)                    # spec 0048: settlement rides the IPD drawer
url, _ = op.post(f"/ipd/discharge/{adm}?handler=Confirm", confirm_fields, page)
check("/billing/invoice/" in url, "the prepared settlement still confirms while blocked")
check(collect_everything(name), "the patient pays in full")
release(adm, "paid in full")
page = op.get(f"/ipd/discharge/{adm}")
op.post(f"/ipd/discharge/{adm}?handler=Discharge", {"AdmissionId": adm}, page)
check("Gate pass" in op.get(f"/ipd/discharge/{adm}"),
      "released and settled, they can leave — the bed is not stuck forever")
free_bed(bed)

# 6 — over-discount --------------------------------------------------------
case(7, "A discount larger than the bill cannot slip through")
name, pid = new_patient("Edge Discount")
page = op.get(f"/billing/opd?PatientId={pid}")
token = SUBMIT_RE.search(page)
catalog = re.search(r'name="catalogId" value="(\d+)"', page)
page = op.post("/billing/opd?handler=Add",
               {"catalogId": catalog.group(1), "PatientId": pid,
                "SubmissionToken": token.group(1)}, page)[1]
url, html = op.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [catalog.group(1)], "SubmissionToken": token.group(1),
    "DiscountFlat": "999999", "DiscountReason": "edge probe",
    "PaidNow": "0", "Tender": "cash"}, page)
check("/billing/invoice/" not in url, "the over-discounted bill is not saved")
check(error_on(html) != "", f"and the operator is told why: {error_on(html)[:80]}")

# 7 — public surface leaks nothing -----------------------------------------
case(8, "The unauthenticated surface answers without leaking")
anon = urllib.request.build_opener()
body = anon.open(BASE + "/public/report-status?orderNo=LB-99999999").read().decode()
check("No report found" in body, "an unknown order number gets the neutral answer")
check("৳" not in body and "&#x9F3;" not in body, "no money on the public page")
queue = anon.open(BASE + "/public/queue").read().decode()
check("&#x9F3;" not in queue and "৳" not in queue, "no money on the lobby display")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("EDGE CASES PASSED — the awkward paths hold")
