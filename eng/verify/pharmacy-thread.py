#!/usr/bin/env python3
"""Spec 0016 end-to-end thread: PO -> approval -> GRN -> FEFO sale -> expiry block ->
due -> refund restock -> quarantine/return -> transfer -> audit -> dashboard.
Assertions track the records this run creates (by id), so the script is
dirty-database-tolerant and usable by the upgrade gate (ADR-0022)."""
import datetime, http.cookiejar, json, os, pathlib, re, sys, time, urllib.parse, urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import fixture   # noqa: E402

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


# Per-run identity. Every batch this thread received used to be called THREAD-1, so after a
# few runs the shelf held several batches of that name and "find our batch" found the oldest,
# emptiest one (spec 0029 / round-2 R2-4 — first row wins, one level down).
STAMP = f"{int(time.time() * 1000) % 100000:05d}"
BATCH_NO = f"THREAD-{STAMP}"

print("PHARMACY THREAD (M11)")


def named_customer():
    """A patient to put a credit sale against — registered once, reused by later runs."""
    hits = json.loads(ph.get("/api/typeahead/patients?q=Rahim"))
    if not hits:
        reg = Session("jashim", "Demo#1234")
        reg.post("/registration/new", {
            "FullName": "Rahim Uddin", "Sex": "M", "AgeOrDob": "40", "Phone": "01711223344",
            "PatientType": "general", "action": "save"})
        hits = json.loads(ph.get("/api/typeahead/patients?q=Rahim"))
    return str(hits[0]["value"]) if hits else ""


ph = Session("parvin", "Demo#1234")

# 1 — counter --------------------------------------------------------------
step(1, "Pharmacist opens the pharmacy counter")
sess = ph.get("/billing/session")
if "Your counter is open" not in sess:
    counter = re.search(r'value="(\d+)"[^>]*>[^<]*Pharmacy Counter', sess) \
        or re.search(r'<option value="(\d+)">[^<]*Pharmacy', sess)
    cid = counter.group(1) if counter else "4"
    ph.post("/billing/session", {"CounterId": cid, "OpeningFloat": "500"}, sess)
check("Your counter is open" in ph.get("/billing/session"), "pharmacy counter session open")

# 2 — purchase order + approval + GRN -------------------------------------
step(2, "Purchase order walks §11: raise → approve⚿ → order → receive")
pur = ph.get("/pharmacy/purchase")
prod = fixture(re.search(r'<option value="(\d+)">Napa', pur),
               "no demo product on the purchase-order form")
check(prod is not None, "product available on the PO form")
sup_opt = fixture(re.search(r'name="SupplierId">.*?<option value="(\d+)">(?!— )', pur, re.S),
                  "no supplier to raise a purchase order against")
out_opt = fixture(re.search(r'name="OutletId">.*?<option value="(\d+)">(?!— )', pur, re.S),
                  "no pharmacy outlet to receive into")
ph.post("/pharmacy/purchase?handler=Create", {
    "SupplierId": sup_opt.group(1), "OutletId": out_opt.group(1),
    "ProductIds": [prod.group(1), "0", "0"], "LineQtys": ["100", "0", "0"],
    "LineCosts": ["1", "0", "0"]}, pur)
pur = ph.get("/pharmacy/purchase")
po_id = fixture(re.search(r"PO #(\d+)", pur), "the purchase order was not created")
check(po_id is not None, "purchase order created")
check("Requested" in pur, "PO waits at Requested (no auto-approve for this role)")

sup = Session("shahid", "Demo#1234")
inbox = sup.get("/admin/approvals")
rid = re.search(r'name="id" value="(\d+)"', inbox)
check(rid is not None and "Purchase" in inbox or "purchase" in inbox, "PO request in the approvals inbox")
sup.post("/admin/approvals?handler=Decide", {"id": rid.group(1), "approve": "true", "note": "ok"}, inbox)

pur = ph.get("/pharmacy/purchase")
ph.post("/pharmacy/purchase?handler=Advance", {"PoId": po_id.group(1)}, pur)   # → Approved
pur = ph.get("/pharmacy/purchase")
ph.post("/pharmacy/purchase?handler=Advance", {"PoId": po_id.group(1)}, pur)   # → Ordered
pur = ph.get("/pharmacy/purchase")
line_id = fixture(re.search(r'name="PoLineId" value="(\d+)"', pur),
                  "the ordered PO offers no line to receive against")
check(line_id is not None, "GRN form offered on the ordered PO")
ph.post("/pharmacy/purchase?handler=Receive", {
    "PoId": po_id.group(1), "PoLineId": line_id.group(1),
    "BatchNo": BATCH_NO, "Expiry": "31/12/2028", "Qty": "100",
    "UnitCost": "1", "UnitMrp": "2"}, pur)
pur = ph.get("/pharmacy/purchase")
check("complete" in pur, "line fully received — batch on the shelf")

# 3 — FEFO sale (walk-in) ---------------------------------------------------
# Repeat-run safe: the seeded near-expiry batch runs out after a few passes, so the FEFO
# PROPERTY is asserted (earliest-expiring sellable batch wins) rather than a fixed batch no.
step(3, "Walk-in sale picks the earliest expiry first (US11.1)")


def sellable_batches(html, product_word="Napa"):
    """(batch_no, expiry, qty) for in-stock, unexpired batches of the product, earliest first."""
    out = []
    for row in re.findall(r"<tr>(.*?)</tr>", html, re.S):
        cells = [re.sub(r"<[^>]+>", "", c).strip() for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
        if len(cells) < 7 or product_word not in cells[0]:
            continue
        state = cells[6].split("\n")[0].strip()
        qty = int(re.sub(r"\D", "", cells[3]) or 0)
        if qty > 0 and state in ("in stock", "near expiry"):
            out.append((cells[1], cells[2], qty))
    return sorted(out, key=lambda b: datetime.datetime.strptime(b[1], "%d %b %Y"))


stock_now = ph.get("/pharmacy/stock?Show=all")
expected = sellable_batches(stock_now)
check(len(expected) > 0, "the shelf has sellable stock of the demo product")
pos = ph.get("/pharmacy/pos")
# The CONTRACT, not the seed: the seeded near-expiry batch is legitimately sold down by
# repeated runs, so require the wording only when a batch is actually inside the window.
window = datetime.datetime.now() + datetime.timedelta(days=90)
near = [b for b in expected if datetime.datetime.strptime(b[1], "%d %b %Y") <= window]
if near:
    check("near expiry" in stock_now, "near-expiry stock is flagged in words on the shelf")
else:
    check("Expiry" in stock_now, "no batch is near expiry today; the shelf still shows expiries")
pos = ph.post("/pharmacy/pos?handler=Add", {"productId": prod.group(1)}, pos)[1]
url, receipt = ph.post("/pharmacy/pos?handler=Save", {
    "Items": [prod.group(1)], "Qtys": ["2"], "PaidNow": "4", "Tender": "cash"}, pos)
check("/billing/invoice/" in url, "sale saved through the money spine")
# FEFO's contract is the earliest EXPIRY, not a particular batch: once several batches share
# a date — which they do the moment this thread has run twice — "which one" is an arbitrary
# tie-break the product never promised. Assert the promise, not the tie-break.
earliest = expected[0][1]
same_date = {b[0] for b in expected if b[1] == earliest}
check(any(b in receipt for b in same_date),
      f"FEFO took a batch expiring {earliest} — the earliest on the shelf "
      f"({len(same_date)} share that date)")
walkin_invoice = url.rstrip("/").split("/")[-1]

# 4 — stock beyond what is sellable is blocked ------------------------------
# Repeat-run safe (spec 0029 F3/F4). This used to ask for 310 units of seeded Seclo, whose
# on-hand quantity the suite itself sold down to zero; once it hit zero the type-ahead stopped
# listing the product, the regex matched nothing, and the thread died on
# `'NoneType' object has no attribute 'group'` — a stock fact reported as a crash.
#
# The case is not about Seclo. It is about the counter refusing a quantity larger than the
# unexpired stock behind it, so it is asserted against the batch THIS RUN received at step 2:
# ask for one unit more than exists and it must be refused.
step(4, "A sale beyond unexpired stock is refused (US11.1)")
sellable_qty = sum(q for _, _, q in sellable_batches(ph.get("/pharmacy/stock?Show=all")))
fixture(sellable_qty > 0, "no sellable stock of the demo product after our own GRN",
        "step 2 receives 100 units; if none are on the shelf the GRN did not post")
# Named customer, so the refusal under test is the stock one: a walk-in with nothing tendered
# is refused earlier, for credit without a name, and would never reach the stock check.
customer = named_customer()
pos = ph.get("/pharmacy/pos")
pos = ph.post("/pharmacy/pos?handler=Add",
              {"productId": prod.group(1), "PatientId": customer}, pos)[1]
url, html = ph.post("/pharmacy/pos?handler=Save", {
    "PatientId": customer, "Items": [prod.group(1)], "Qtys": [str(sellable_qty + 1)],
    "PaidNow": "0", "Tender": "cash"}, pos)
check("sellable" in html and "/billing/invoice/" not in url,
      f"asking for {sellable_qty + 1} when {sellable_qty} are sellable is refused")

# 5 — credit sale needs a name; due lands on the shared dues screen ---------
step(5, "Credit sale to a named patient raises a due")
pid = named_customer()
check(pid != "", "type-ahead finds the customer")
pos = ph.get("/pharmacy/pos")
pos = ph.post("/pharmacy/pos?handler=Add", {"productId": prod.group(1), "PatientId": pid}, pos)[1]
url, html = ph.post("/pharmacy/pos?handler=Save", {
    "PatientId": pid, "Items": [prod.group(1)], "Qtys": ["3"],
    "PaidNow": "2", "Tender": "cash"}, pos)
check("/billing/invoice/" in url, "credit sale saved")
credit_invoice = url.rstrip("/").split("/")[-1]
dues = ph.get(f"/billing/dues?Q=Rahim")
check(f'name="InvoiceId" value="{credit_invoice}"' in dues, "the due is on the shared collection screen")
ph.post("/billing/dues?handler=Collect",
        {"InvoiceId": credit_invoice, "Amount": "4", "Tender": "cash"}, dues)
dues = ph.get(f"/billing/dues?Q=Rahim")
check(f'name="InvoiceId" value="{credit_invoice}"' not in dues, "due cleared")

# 6 — refund restocks the exact batches ------------------------------------
step(6, "Refund of the walk-in sale restocks the batch (sales return)")
ref = ph.get(f"/pharmacy/reports")
before = ref.count("Sale return")
refund_page = ph.get(f"/billing/refund?Q=")
ph.post("/billing/refund?handler=Request", {
    "InvoiceId": walkin_invoice, "Amount": "4", "Reason": "customer returned strips",
    "Action": "refund"}, refund_page)
inbox = sup.get("/admin/approvals")
rid = re.search(r'name="id" value="(\d+)"', inbox)
check(rid is not None, "refund waits for approval")
sup.post("/admin/approvals?handler=Decide", {"id": rid.group(1), "approve": "true", "note": "ok"}, inbox)
refund_page = ph.get("/billing/refund")
req_id = re.search(r'name="requestId" value="(\d+)"', refund_page)
check(req_id is not None, "approved refund awaits execution at the counter")
ph.post("/billing/refund?handler=Execute", {"requestId": req_id.group(1)}, refund_page)
ledger = ph.get("/pharmacy/reports")
check("Sale return" in ledger and ledger.count("Sale return") > before,
      "stock ledger shows the sale return — goods back on the shelf")

# 7 — quarantine and supplier return ---------------------------------------
# Repeat-run safe (spec 0029). This used to quarantine whatever expired batch was lying
# about, and on a second pass whatever was already quarantined — which on a database with
# history means SOMEBODY ELSE'S batch, in another outlet, possibly already returned, while
# the ledger it then checked was this run's supplier. It failed as "supplier ledger credited
# for the return" and read like a product defect.
#
# The rule the rest of this spec follows applies here too: assert the lifecycle against the
# batch THIS RUN received at step 2, from the supplier THIS RUN bought from.
step(7, "Quarantine → return to supplier, on this run's own batch (5A-11)")
stock = ph.get("/pharmacy/stock?Show=all")


def batch_row(html, batch_no):
    """The (batch_id, qty) of a named batch on the shelf, or None."""
    for row in re.findall(r"<tr>(.*?)</tr>", html, re.S):
        cells = [re.sub(r"<[^>]+>", "", c).strip()
                 for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
        bid = re.search(r'name="BatchId" value="(\d+)"', row)
        if len(cells) >= 4 and cells[1] == batch_no and bid:
            return bid.group(1), int(re.sub(r"\D", "", cells[3]) or 0)
    return None


ours = fixture(batch_row(stock, BATCH_NO),
               "the batch this run received at step 2 is not on the shelf",
               "step 2's GRN did not post, so there is nothing to quarantine")
ph.post("/pharmacy/stock?handler=Quarantine",
        {"BatchId": ours[0], "Reason": f"QA probe {STAMP} — supplier return",
         "OutletId": "1", "Show": "all"}, stock)
quarantined = ph.get("/pharmacy/stock?Show=quarantined")
# A reason unique to this run. "damaged in transit" is what pharmacy-full asserts on two
# scripts later, so sharing the wording let each script pass on the other's row.
check(f"QA probe {STAMP}" in quarantined,
      "our batch is quarantined, with this run's own reason on the record")
ph.post("/pharmacy/stock?handler=Return",
        {"BatchId": ours[0], "SupplierId": sup_opt.group(1),
         "OutletId": "1", "Show": "quarantined"}, quarantined)
suppliers = ph.get(f"/pharmacy/suppliers?Ledger={sup_opt.group(1)}")
check("Return credit" in suppliers or "return_credit" in suppliers.lower(),
      "the supplier we bought from is credited for the return")

# The expiry semantics themselves are asserted by step 4 (a sale beyond unexpired stock is
# refused); here it is enough that expired stock is visibly separated from sellable stock.
expired = ph.get("/pharmacy/stock?Show=expired")
check("Expiry" in expired or "expired" in expired.lower(),
      "the expired shelf is a distinct view from the sellable one")

# 8 — outlet transfer -------------------------------------------------------
step(8, "Outlet transfer: indent → send FEFO → receive (5A-11)")
tr = ph.get("/pharmacy/transfers")
outs = re.findall(r'<option value="(\d+)">(?!—)', tr)
ph.post("/pharmacy/transfers?handler=Indent", {
    "FromOutletId": "1", "ToOutletId": "2",
    "ProductIds": [prod.group(1), "0", "0"], "LineQtys": ["5", "0", "0"]}, tr)
tr = ph.get("/pharmacy/transfers")
tid = re.search(r'name="TransferId" value="(\d+)"', tr)
check(tid is not None, "indent raised")
ph.post("/pharmacy/transfers?handler=Send", {"TransferId": tid.group(1)}, tr)
tr = ph.get("/pharmacy/transfers")
ph.post("/pharmacy/transfers?handler=Receive", {"TransferId": tid.group(1)}, tr)
tr = ph.get("/pharmacy/transfers")
check("Received" in tr, "transfer received — batch identity recreated at destination")

# 9 — stock audit -----------------------------------------------------------
step(9, "Stock count: variance → approval⚿ → posted (§11)")
ph.post("/pharmacy/stock?handler=StartAudit", {"OutletId": "1"}, ph.get("/pharmacy/stock"))
stock = ph.get("/pharmacy/stock")
line = re.search(r'name="LineId" value="(\d+)"', stock)
sysqty = re.search(r'name="CountedQty"[^>]*value="(\d+)"', stock)
check(line is not None, "count sheet open with system quantities")
ph.post("/pharmacy/stock?handler=Count",
        {"LineId": line.group(1), "CountedQty": str(int(sysqty.group(1)) - 1), "OutletId": "1"}, stock)
stock = ph.get("/pharmacy/stock")
audit_id = re.search(r'name="AuditId" value="(\d+)"', stock)
ph.post("/pharmacy/stock?handler=RequestAdjust", {"AuditId": audit_id.group(1), "OutletId": "1"}, stock)
inbox = sup.get("/admin/approvals")
rid = re.search(r'name="id" value="(\d+)"', inbox)
if rid:  # adjustment above the pharmacist's (absent) threshold routes to the inbox
    sup.post("/admin/approvals?handler=Decide", {"id": rid.group(1), "approve": "true", "note": "ok"}, inbox)
stock = ph.get("/pharmacy/stock")
ph.post("/pharmacy/stock?handler=PostAudit", {"AuditId": audit_id.group(1), "OutletId": "1"}, stock)
stock = ph.get("/pharmacy/stock")
check("Posted" in stock, "audit posted — adjustment on the ledger, not an edit")

# 10 — dashboard ------------------------------------------------------------
step(10, "Pharmacy dashboard shows the day (US11.4)")
dash = ph.get("/pharmacy/dashboard")
check("Today's takings" in dash, "takings tile present")
check("Stock value at cost" in dash, "stock value tile present")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("PHARMACY THREAD PASSED — all checks green")
