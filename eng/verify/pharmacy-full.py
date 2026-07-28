#!/usr/bin/env python3
"""Every feature of M11 Pharmacy, walked end to end — one row per line of the spec 0016
traceability matrix, so "is the pharmacy module working?" has an evidenced answer rather than
an impression.

`pharmacy-thread.py` walks the module's spine (PO → GRN → FEFO sale → refund → transfer →
audit). This walks the REST: creating companies and products, the reorder shortlist, supplier
payments and replacements, the sales/purchase statements, outlet creation, the damage
write-off with its approval, and the staff-sale variant.

Dirty-database tolerant and repeat-run safe: everything it needs, it creates."""
import http.cookiejar, json, os, pathlib, re, sys, time, urllib.parse, urllib.request

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from _harness import record, tag   # noqa: E402

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


def feature(row, text):
    print(f"  [{row:>4}] {text}")


def check(cond, msg):
    print(f"         {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(f"{msg}")


def error_on(html):
    m = ERR_RE.search(html)
    return re.sub(r"<[^>]+>", "", m.group(1)).strip() if m else ""


print("PHARMACY MODULE — EVERY FEATURE (spec 0016 matrix rows 1–17)")

ph = Session("parvin", "Demo#1234")     # pharmacist
sup = Session("shahid", "Demo#1234")    # supervisor (approvals)
fd = Session("jashim", "Demo#1234")     # front desk (patients)

stamp = f"{int(time.time() * 1000) % 100000:05d}"

# counter custody the POS rides
session_page = ph.get("/billing/session")
if "Your counter is open" not in session_page:
    m = re.search(r'value="(\d+)"[^>]*>[^<]*Pharmacy', session_page)
    ph.post("/billing/session",
            {"CounterId": m.group(1) if m else "4", "OpeningFloat": "1000"}, session_page)


def approve(label):
    """Approve the newest request of the given humanized type; returns True if one was found."""
    inbox = sup.get("/admin/approvals")
    ids = re.findall(label + r'.*?name="id" value="(\d+)"', inbox, re.S)
    if not ids:
        return False
    sup.post("/admin/approvals?handler=Decide",
             {"id": max(ids, key=int), "approve": "true", "note": "ok"}, inbox)
    return True


# ---- row 1: company & product registration -------------------------------
feature("1", "Company & product registration (/pharmacy/products)")
page = ph.get("/pharmacy/products")
company_name = tag(f"Probe Pharma {stamp}")
ph.post("/pharmacy/products?handler=Company", {"CompanyName": company_name}, page)
page = ph.get("/pharmacy/products")
check(company_name in page, "a new company appears in the master")
company_id = re.search(r'<option value="(\d+)">' + re.escape(company_name), page)
check(company_id is not None, "the new company is selectable when adding a product")

brand = f"Probecillin {stamp}"
ph.post("/pharmacy/products?handler=Create", {
    "CompanyId": company_id.group(1), "Brand": brand, "Generic": "Probe-generic",
    "Strength": "250 mg", "Form": "tablet", "Unit": "pcs", "ReorderLevel": "40"}, page)
page = ph.get("/pharmacy/products?Q=" + urllib.parse.quote(brand))
check(brand in page, "the new product appears in the catalogue")
product_id = re.search(r'name="ProductId" value="(\d+)"', page)
check(product_id is not None, "the product carries an id for stock operations")
pid_new = product_id.group(1)

# ---- row 2: PO → GRN, on the NEW product ---------------------------------
feature("2", "Purchase order → GRN creates a batch of the new product")
purchase = ph.get("/pharmacy/purchase")
supplier_opt = re.search(r'name="SupplierId">.*?<option value="(\d+)">(?!— )', purchase, re.S)
outlet_opt = re.search(r'name="OutletId">.*?<option value="(\d+)">(?!— )', purchase, re.S)
check(supplier_opt is not None and outlet_opt is not None, "supplier and outlet are selectable")
before_pos = set(re.findall(r"PO #(\d+)", purchase))
ph.post("/pharmacy/purchase?handler=Create", {
    "SupplierId": supplier_opt.group(1), "OutletId": outlet_opt.group(1),
    "ProductIds": [pid_new, "0", "0"], "LineQtys": ["60", "0", "0"],
    "LineCosts": ["10", "0", "0"]}, purchase)
purchase = ph.get("/pharmacy/purchase")
mine = sorted(set(re.findall(r"PO #(\d+)", purchase)) - before_pos, key=int)
check(len(mine) > 0, "the purchase order was raised")
po_id = mine[-1]
check(approve("Purchase"), "the PO waits for approval and the supervisor decides it")
for _ in range(2):                                   # Approved → Ordered
    purchase = ph.get("/pharmacy/purchase")
    ph.post("/pharmacy/purchase?handler=Advance", {"PoId": po_id}, purchase)
purchase = ph.get("/pharmacy/purchase")
line = re.search(r'name="PoLineId" value="(\d+)"', purchase)
check(line is not None, "the ordered PO offers a goods-receive form")
batch_no = f"PRB-{stamp}"
ph.post("/pharmacy/purchase?handler=Receive", {
    "PoId": po_id, "PoLineId": line.group(1), "BatchNo": batch_no,
    "Expiry": "31/12/2029", "Qty": "60", "UnitCost": "10", "UnitMrp": "18"}, purchase)
stock_all = ph.get("/pharmacy/stock?Show=all&Q=" + urllib.parse.quote(brand))
check(batch_no in stock_all, "the received batch is on the shelf with its own batch number")

# ---- row 7: auto reorder shortlist (US11.3) ------------------------------
feature("7", "Auto reorder shortlist (US11.3) — reorder level vs on hand")
reports = ph.get("/pharmacy/reports")
check("Reorder shortlist" in reports, "the shortlist section renders")
# 60 on hand against a reorder level of 40: the new product must NOT be short yet.
shortlist = reports[reports.index("Reorder shortlist"):]
shortlist = shortlist[:shortlist.index("Stock ledger")] if "Stock ledger" in shortlist else shortlist
check(brand not in shortlist, "a well-stocked product is absent from the shortlist")
# Sell it down below the reorder level, then it must appear.
pos = ph.get("/pharmacy/pos?Q=" + urllib.parse.quote(brand))
token = SUBMIT_RE.search(pos)
prod_ctl = re.search(r'name="productId" value="(' + pid_new + r')"', pos)
check(prod_ctl is not None, "the new product is sellable at the counter")
pos = ph.post("/pharmacy/pos?handler=Add",
              {"productId": pid_new, "Q": brand, "SubmissionToken": token.group(1)}, pos)[1]
url, html = ph.post("/pharmacy/pos?handler=Save", {
    "Items": [pid_new], "Qtys": ["25"], "PaidNow": str(25 * 18), "Tender": "cash",
    "SubmissionToken": token.group(1)}, pos)
check("/billing/invoice/" in url, f"sold 25 units ({error_on(html)[:60]})")
reports = ph.get("/pharmacy/reports")
shortlist = reports[reports.index("Reorder shortlist"):]
shortlist = shortlist[:shortlist.index("Stock ledger")] if "Stock ledger" in shortlist else shortlist
check(brand in shortlist, "once stock falls to the reorder level it appears on the shortlist")

# ---- row 17: staff-pharmacy sale variant ---------------------------------
feature("17", "Staff-pharmacy sale variant (5A-11) — tagged and attributable")
patients = json.loads(ph.get("/api/typeahead/patients?q=Rahim"))
if not patients:
    fd.post("/registration/new", {
        "FullName": "Rahim Uddin", "Sex": "M", "AgeOrDob": "40", "Phone": "01711223344",
        "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"})
    patients = json.loads(ph.get("/api/typeahead/patients?q=Rahim"))
staff_pid = str(patients[0]["value"])
pos = ph.get(f"/pharmacy/pos?PatientId={staff_pid}")
check('name="StaffSale"' in pos, "the counter offers the staff-sale variant")
token = SUBMIT_RE.search(pos)
pos = ph.post("/pharmacy/pos?handler=Add",
              {"productId": pid_new, "PatientId": staff_pid,
               "SubmissionToken": token.group(1)}, pos)[1]
url, html = ph.post("/pharmacy/pos?handler=Save", {
    "PatientId": staff_pid, "Items": [pid_new], "Qtys": ["1"],
    "StaffSale": "true", "DiscountFlat": "3", "DiscountReason": "staff rate",
    "PaidNow": "15", "Tender": "cash", "SubmissionToken": token.group(1)}, pos)
staff_ok = "/billing/invoice/" in url
if not staff_ok:                        # a discount above the role limit parks for approval
    staff_ok = approve("Discount")
    if staff_ok:
        pos = ph.get(f"/pharmacy/pos?PatientId={staff_pid}")
        token = SUBMIT_RE.search(pos)
        pos = ph.post("/pharmacy/pos?handler=Add",
                      {"productId": pid_new, "PatientId": staff_pid,
                       "SubmissionToken": token.group(1)}, pos)[1]
        url, html = ph.post("/pharmacy/pos?handler=Save", {
            "PatientId": staff_pid, "Items": [pid_new], "Qtys": ["1"],
            "StaffSale": "true", "DiscountFlat": "3", "DiscountReason": "staff rate",
            "PaidNow": "15", "Tender": "cash", "SubmissionToken": token.group(1)}, pos)
        staff_ok = "/billing/invoice/" in url
check(staff_ok, f"a staff sale saves, tagged and discounted through the approval engine")
# The tag must survive a staff sale with NO discount — a staff member paying full MRP.
pos = ph.get(f"/pharmacy/pos?PatientId={staff_pid}")
token = SUBMIT_RE.search(pos)
pos = ph.post("/pharmacy/pos?handler=Add",
              {"productId": pid_new, "PatientId": staff_pid,
               "SubmissionToken": token.group(1)}, pos)[1]
url, html = ph.post("/pharmacy/pos?handler=Save", {
    "PatientId": staff_pid, "Items": [pid_new], "Qtys": ["1"], "StaffSale": "true",
    "PaidNow": "18", "Tender": "cash", "SubmissionToken": token.group(1)}, pos)
check("/billing/invoice/" in url, "a staff sale at full price saves")
undiscounted_invoice = url.rstrip("/").split("/")[-1]
audit_page = Session("admin", "Demo#1234").get("/admin/audit?Q=pharmacy.sale.staff")
check("pharmacy.sale.staff" in audit_page,
      "the staff tag is a recorded fact, not a side effect of discounting (P17 pending)")

# ---- rows 8/16: supplier ledger, payment, replacement --------------------
feature("8", "Supplier ledger & payment (/pharmacy/suppliers)")
suppliers = ph.get("/pharmacy/suppliers")
supplier_name = tag(f"Probe Supplies {stamp}")
ph.post("/pharmacy/suppliers?handler=Create",
        {"Name": supplier_name, "Phone": "01700000000"}, suppliers)
suppliers = ph.get("/pharmacy/suppliers")
check(supplier_name in suppliers, "a new supplier can be registered")
ledger_supplier = supplier_opt.group(1)
ledger = ph.get(f"/pharmacy/suppliers?Ledger={ledger_supplier}")
check("payable" in ledger.lower() or "Receipt" in ledger,
      "the supplier ledger shows what the receipt made payable")
_, paid_html = ph.post("/pharmacy/suppliers?handler=Pay", {
    "SupplierId": ledger_supplier, "Amount": "100", "Note": f"probe payment {stamp}"},
    ph.get("/pharmacy/suppliers"))
ledger = ph.get(f"/pharmacy/suppliers?Ledger={ledger_supplier}")
check(f"probe payment {stamp}" in ledger or "Payment" in ledger,
      "a payment posts to the supplier ledger and is visible")

feature("16", "Supplier replacement (5A-11) — return for replacement, not for credit")
stock_page = ph.get("/pharmacy/stock?Show=all&Q=" + urllib.parse.quote(brand))
batch_ctl = re.search(r'name="BatchId" value="(\d+)"', stock_page)
check(batch_ctl is not None, "the batch offers stock actions")
# The stock screen is scoped to one outlet and both handlers redirect back to it, so the
# outlet has to travel with the post — omitting it sent the return to whichever outlet the
# page defaulted to and the credit landed nowhere this check could see it.
outlet_ctl = re.search(r'name="OutletId" value="(\d+)"', stock_page)
outlet_id = outlet_ctl.group(1) if outlet_ctl else "1"
ph.post("/pharmacy/stock?handler=Quarantine", {
    "BatchId": batch_ctl.group(1), "Reason": "damaged in transit",
    "OutletId": outlet_id, "Show": "all", "Q": brand}, stock_page)
quarantined = ph.get("/pharmacy/stock?Show=quarantined")
check("damaged in transit" in quarantined, "damage quarantine records the reason (row 14)")
ph.post("/pharmacy/stock?handler=Return", {
    "BatchId": batch_ctl.group(1), "SupplierId": ledger_supplier, "OutletId": outlet_id,
    "Replacement": "true", "Show": "quarantined"}, quarantined)
ledger = ph.get(f"/pharmacy/suppliers?Ledger={ledger_supplier}")
check("eplacement" in ledger, "the return is recorded as awaiting replacement, not as credit")

# ---- row 14: damage write-off (Dispose⚿) ---------------------------------
feature("14", "Damage management → approval-gated write-off (Disposed, logged)")
purchase = ph.get("/pharmacy/purchase")
before_pos = set(re.findall(r"PO #(\d+)", purchase))
ph.post("/pharmacy/purchase?handler=Create", {
    "SupplierId": supplier_opt.group(1), "OutletId": outlet_opt.group(1),
    "ProductIds": [pid_new, "0", "0"], "LineQtys": ["20", "0", "0"],
    "LineCosts": ["10", "0", "0"]}, purchase)
purchase = ph.get("/pharmacy/purchase")
mine = sorted(set(re.findall(r"PO #(\d+)", purchase)) - before_pos, key=int)
po2 = mine[-1]
approve("Purchase")
for _ in range(2):
    purchase = ph.get("/pharmacy/purchase")
    ph.post("/pharmacy/purchase?handler=Advance", {"PoId": po2}, purchase)
purchase = ph.get("/pharmacy/purchase")
line2 = re.search(r'name="PoLineId" value="(\d+)"', purchase)
dispose_batch = f"DMG-{stamp}"
ph.post("/pharmacy/purchase?handler=Receive", {
    "PoId": po2, "PoLineId": line2.group(1), "BatchNo": dispose_batch,
    "Expiry": "31/12/2029", "Qty": "20", "UnitCost": "10", "UnitMrp": "18"}, purchase)
stock_page = ph.get("/pharmacy/stock?Show=all&Q=" + urllib.parse.quote(brand))
dmg = re.search(r'name="BatchId" value="(\d+)"[\s\S]{0,4000}?' + re.escape(dispose_batch), stock_page) \
    or re.search(re.escape(dispose_batch) + r'[\s\S]{0,4000}?name="BatchId" value="(\d+)"', stock_page)
check(dmg is not None, "the damaged batch is actionable on the stock screen")
ph.post("/pharmacy/stock?handler=Quarantine", {
    "BatchId": dmg.group(1), "Reason": "water damage", "Show": "all", "Q": brand}, stock_page)
quarantined = ph.get("/pharmacy/stock?Show=quarantined")
_, out = ph.post("/pharmacy/stock?handler=Dispose",
                 {"BatchId": dmg.group(1), "Show": "quarantined"}, quarantined)
check("approv" in (error_on(out) + error_on(ph.get("/pharmacy/stock?Show=quarantined"))).lower(),
      "disposing without an approval is refused (verify-don't-trust)")
quarantined = ph.get("/pharmacy/stock?Show=quarantined")
ph.post("/pharmacy/stock?handler=RequestWriteoff",
        {"BatchId": dmg.group(1), "Reason": "water damage", "Show": "quarantined"}, quarantined)
check(approve("Stock-writeoff"), "the write-off request reaches the approvals inbox")
quarantined = ph.get("/pharmacy/stock?Show=quarantined")
ph.post("/pharmacy/stock?handler=Dispose",
        {"BatchId": dmg.group(1), "Show": "quarantined"}, quarantined)
disposed = ph.get("/pharmacy/stock?Show=all&Q=" + urllib.parse.quote(brand))
check("Disposed" in disposed, "the approved write-off disposes the batch, logged")

# ---- rows 12/13: outlets master + transfer ledger ------------------------
feature("13", "Outlets master + outlet transfer ledger (5A-11)")
transfers = ph.get("/pharmacy/transfers")
outlet_name = tag(f"Probe Outlet {stamp}")
ph.post("/pharmacy/transfers?handler=Outlet",
        {"OutletName": outlet_name, "OutletKind": "sub"}, transfers)
transfers = ph.get("/pharmacy/transfers")
check(outlet_name in transfers, "a new outlet can be created in the master")
check("Transfer ledger" in transfers, "the outlet transfer ledger renders")

# ---- row 9: statements + stock ledger ------------------------------------
feature("9", "Sales & purchase statements + periodical stock ledger")
reports = ph.get("/pharmacy/reports")
check("Sales statement" in reports, "the sales statement renders")
check("Purchase statement" in reports, "the purchase statement renders")
check("Stock ledger" in reports, "the periodical stock ledger renders")
check(brand in reports or batch_no in reports,
      "today's own movements appear in the reports")
today = time.strftime("%d/%m/%Y")
ranged = ph.get(f"/pharmacy/reports?from={urllib.parse.quote(today)}&to={urllib.parse.quote(today)}")
check("Sales statement" in ranged, "the statements accept a typed date range (ADR-0020)")

# ---- row 10: dashboard ---------------------------------------------------
feature("10", "Pharmacy dashboard (US11.4)")
dash = ph.get("/pharmacy/dashboard")
check("Today's takings" in dash, "takings tile")
check("Stock value at cost" in dash, "stock value tile")
check("expiry" in dash.lower(), "near-expiry tile")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("PHARMACY MODULE PASSED — every matrix row exercised")
