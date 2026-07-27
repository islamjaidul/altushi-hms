#!/usr/bin/env python3
"""The F2 demo beat: OPD bill, discount above the operator's limit, supervisor approval, due collection."""
import json, re, sys, urllib.parse, http.cookiejar, urllib.request

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


def check(cond, msg):
    print(f"      {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


print("DISCOUNT APPROVAL + DUE COLLECTION")

bill = Session("rasel", "Demo#1234")

# The counter from the golden thread was closed, so open a fresh one.
sess = bill.get("/billing/session")
if "Your counter is open" not in sess:
    bill.post("/billing/session", {"CounterId": "1", "OpeningFloat": "1000"}, sess)
    check("Your counter is open" in bill.get("/billing/session"), "counter reopened after day-close")

print("  1. OPD bill with a discount above the operator's limit")
opd = bill.get("/billing/opd")
# Patient pickers are the shared type-ahead now (ADR-0020, §7 U5).
hits = json.loads(bill.get("/api/typeahead/patients?q=Rahim"))
check(len(hits) > 0, "type-ahead finds the patient at the counter")
pid = str(hits[0]["value"])
opd = bill.post("/billing/opd", {"PatientId": pid}, opd)[1]

rows = re.findall(r'<span class="cat-code">([A-Z\-0-9]+)</span>.*?name="catalogId" value="(\d+)"', opd, re.S)
cat = {c: i for c, i in rows}
check("CON-SPC" in cat, f"service catalogue priced ({len(cat)} items)")

opd = bill.post("/billing/opd", {"PatientId": pid, "Items": [cat["CON-SPC"]],
                                 "catalogId": cat["CON-SPC"], "handler": "Add"}, opd)[1]
gross = re.search(r'data-gross="(\d+)"', opd)
check(gross and gross.group(1) == "1200", f"gross ৳{gross.group(1)}")

# Billing Operator's discount threshold is ৳200 (seeded policy) — ৳500 must escalate.
url, html = bill.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [cat["CON-SPC"]],
    "DiscountFlat": "500", "DiscountReason": "Long-standing patient",
    "PaidNow": "0", "Tender": "cash"}, opd)
check("above your limit" in html, "discount above the limit is refused and escalated")
check("approvals inbox" in html, "operator is told where the request went")

# §5 M4 [M]: a discount without a stated reason is refused outright.
_, no_reason = bill.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [cat["CON-SPC"]],
    "DiscountFlat": "500", "PaidNow": "0", "Tender": "cash"}, opd)
check("needs a reason" in no_reason, "discount without a reason is refused")

print("  2. Supervisor approves in the inbox")
sup = Session("shahid", "Demo#1234")
inbox = sup.get("/admin/approvals")
check("Discount" in inbox, "request is waiting in the supervisor's inbox")
rid = re.search(r'name="id" value="(\d+)"', inbox)
check(rid is not None, "approve control present")
sup.post("/admin/approvals?handler=Decide",
         {"id": rid.group(1), "approve": "true", "note": "Regular patient"}, inbox)
check("Nothing waiting" in sup.get("/admin/approvals"), "inbox cleared after the decision")

print("  3. Counter completes the bill with the approved discount")
opd = bill.get(f"/billing/opd?patientId={pid}")
check("is approved for this" in opd, "counter sees the approval without being told")
opd = bill.post("/billing/opd", {"PatientId": pid, "Items": [cat["CON-SPC"]],
                                 "catalogId": cat["CON-SPC"], "handler": "Add"}, opd)[1]
url, html = bill.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [cat["CON-SPC"]],
    "DiscountFlat": "500", "DiscountReason": "Long-standing patient",
    "PaidNow": "300", "Tender": "cash"}, opd)
check("/billing/invoice/" in url, "invoice created")
check("&#x9F3; 500" in html, "discount printed on the receipt")
check("&#x9F3; 400" in html, "due of ৳400 carried (1200 − 500 − 300)")
check("Taka Only" in html, "amount in words on the receipt")
# Track THE invoice this run created — the database may hold other dues (upgrade gate runs
# this against restored data, ADR-0022), so every later assertion is by id, not by name.
inv_id = re.search(r"/billing/invoice/(\d+)", url).group(1)

print("  4. Due collected later")
dues = bill.get("/billing/dues")
check(f'name="InvoiceId" value="{inv_id}"' in dues, "the due is on the collection list")
url, html = bill.post("/billing/dues?handler=Collect",
                      {"InvoiceId": inv_id, "Amount": "400", "Tender": "bkash"}, dues)
check("/billing/invoice/" in url, "collection receipt issued")
dues_after = bill.get("/billing/dues")
check(f'name="InvoiceId" value="{inv_id}"' not in dues_after, "due cleared")

print("  5. Over-collection is impossible")
opd = bill.get(f"/billing/opd?patientId={pid}")
opd = bill.post("/billing/opd", {"PatientId": pid, "Items": [cat["CON-GEN"]],
                                 "catalogId": cat["CON-GEN"], "handler": "Add"}, opd)[1]
url, html = bill.post("/billing/opd?handler=Save", {
    "PatientId": pid, "Items": [cat["CON-GEN"]],
    "DiscountFlat": "0", "PaidNow": "0", "Tender": "cash"}, opd)
inv2 = url.rstrip("/").split("/")[-1]
dues = bill.get("/billing/dues")
url, html = bill.post("/billing/dues?handler=Collect",
                      {"InvoiceId": inv2, "Amount": "99999", "Tender": "cash"}, dues)
check("exceeds due balance" in html, "paying more than the balance is rejected with a plain message")

print()
if fail:
    print(f"FAILED {len(fail)}:")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("DISCOUNT + DUE PATH PASSED")
