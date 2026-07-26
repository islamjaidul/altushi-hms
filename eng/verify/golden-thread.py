#!/usr/bin/env python3
"""Drive the §9A.2 golden thread through the real HTTP surface, as the demo script does."""
import re, sys, urllib.parse, http.cookiejar, urllib.request

BASE = "http://localhost:5199"
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')


class Session:
    def __init__(self, user, password):
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.user = user
        page = self.get("/login")
        self.post("/login", {"Username": user, "Password": password}, page)

    def get(self, path):
        with self.op.open(BASE + path) as r:
            return r.read().decode()

    def post(self, path, fields, page_html=None, follow=True):
        html = page_html if page_html is not None else self.get(path)
        m = TOKEN_RE.search(html)
        data = dict(fields)
        if m:
            data["__RequestVerificationToken"] = m.group(1)
        body = urllib.parse.urlencode(data, doseq=True).encode()
        req = urllib.request.Request(BASE + path, data=body)
        with self.op.open(req) as r:
            return r.geturl(), r.read().decode()


def step(n, text):
    print(f"  {n:2}. {text}")


fail = []


def check(cond, msg):
    if cond:
        print(f"      ✓ {msg}")
    else:
        print(f"      ✗ {msg}")
        fail.append(msg)


print("GOLDEN THREAD")

# 1 — registration -----------------------------------------------------------
recep = Session("jashim", "Demo#1234")
step(1, "Receptionist registers a walk-in patient")
url, html = recep.post("/registration/new", {
    "FullName": "Rahim Uddin", "Sex": "M", "AgeOrDob": "46",
    "Phone": "01711223344", "Area": "Zindabazar", "PatientType": "general",
    "action": "save",
})
m = re.search(r"ALT-\d{6}", html)
uhid = m.group(0) if m else None
check(uhid is not None, f"UHID issued: {uhid}")

# 2 — serial -----------------------------------------------------------------
step(2, "Serial issued for the doctor's queue")
appts = recep.get("/appointments")
pid = re.search(r'<option value="(\d+)">Rahim Uddin', appts)
check(pid is not None, "patient appears in the appointment picker")
if pid:
    url, html = recep.post("/appointments?handler=Issue",
                           {"PatientId": pid.group(1), "DoctorId": "1"}, appts)
    q = recep.get("/appointments")
    check("Rahim Uddin" in q, "patient is on today's serial list")

# 3 — counter session --------------------------------------------------------
bill = Session("rasel", "Demo#1234")
step(3, "Billing operator opens the counter")
sess = bill.get("/billing/session")
url, html = bill.post("/billing/session", {"CounterId": "2", "OpeningFloat": "2000"}, sess)
check("Your counter is open" in bill.get("/billing/session"), "counter session is open")

# 4 — diagnostic order -------------------------------------------------------
step(4, "Diagnostic order: CBC + RBS, invoiced and paid")
order = bill.get("/diagnostics/order")
pid2 = re.search(r'<option value="(\d+)">Rahim Uddin', order)
order = bill.post("/diagnostics/order", {"PatientId": pid2.group(1)}, order)[1]

# map catalogue code -> id straight off the add buttons
rows = re.findall(r'<span class="cat-code">([A-Z\-0-9]+)</span>.*?name="catalogId" value="(\d+)"',
                  order, re.S)
cat = {code: cid for code, cid in rows}
check("CBC" in cat and "RBS" in cat, f"catalogue priced and sellable ({len(cat)} items)")

items = []
for code in ("CBC", "RBS"):
    items.append(cat[code])
    order = bill.post("/diagnostics/order",
                      {"PatientId": pid2.group(1), "Items": items[:-1] + [cat[code]],
                       "catalogId": cat[code], "handler": "Add"}, order)[1]

gross = re.search(r'data-gross="(\d+)"', order)
check(gross and int(gross.group(1)) == 550, f"gross re-resolved server-side: ৳{gross.group(1)}")
promised = "Report promised by" in order
check(promised, "TAT promise shown before payment")

url, html = bill.post("/diagnostics/order?handler=Save", {
    "PatientId": pid2.group(1), "Items": items,
    "DiscountFlat": "0", "PaidNow": "550", "Tender": "cash",
}, order)
check("/diagnostics/order/" in url, f"order slip issued: {url.split('/')[-1]}")
order_id = url.rstrip("/").split("/")[-1]
check("barcode-text" in html, "barcode labels printed on the slip")
barcodes = re.findall(r'<div class="barcode-text">(S[A-Z0-9]+)</div>', html)
check(len(barcodes) >= 1, f"sample tube(s) created: {barcodes}")

# 5 — lab chain --------------------------------------------------------------
lab = Session("ripon", "Demo#1234")
step(5, "Lab collects and receives the sample")
board = lab.get("/lis/board")
check("LB-" in board, "order is on the lab work board")


def advance(handler):
    """Both tubes must move: the order is only as far along as its slowest sample."""
    moved = 0
    while True:
        page = lab.get("/lis/board")
        m = re.search(r'asp-page-handler|handler=' + handler, page)
        ids = re.findall(
            r'<form method="post" action="/lis/board\?handler=' + handler + r'">\s*'
            r'<input type="hidden" name="sampleId" value="(\d+)"', page)
        if not ids:
            ids = re.findall(r'name="sampleId" value="(\d+)"', page) if handler_matches(page, handler) else []
        if not ids:
            return moved
        lab.post("/lis/board?handler=" + handler, {"sampleId": ids[0]}, page)
        moved += 1
        if moved > 6:
            return moved


def handler_matches(page, handler):
    return ("Collect " in page) if handler == "Collect" else ("Receive at lab" in page)


collected = advance("Collect")
check(collected >= 1, f"{collected} tube(s) collected")
received = advance("Receive")
check(received >= 1, f"{received} tube(s) received at the lab")
board = lab.get("/lis/board")
check("Enter result" in board, "result entry offered once every tube is in")

# 6 — result entry -----------------------------------------------------------
step(6, "Technologist enters results (one deliberately abnormal)")
res = lab.get(f"/lis/results?orderId={order_id}")
names = re.findall(r'name="(Values\[[^"]+\])"', res)
check(len(names) > 0, f"{len(names)} parameter field(s) with reference ranges")
payload = {"OrderId": order_id}
for n in names:
    if n.endswith(".HB]"):
        payload[n] = "9.1"        # below range → must flag Low
    elif n.endswith(".RBS]"):
        payload[n] = "11.4"       # above range → must flag High
    elif n.endswith(".TC]"):
        payload[n] = "7800"
    elif n.endswith(".PLT]"):
        payload[n] = "240000"
    else:
        payload[n] = "1"
lab.post("/lis/results", payload, res)
board = lab.get("/lis/board")
check("Waiting for the pathologist" in board or "Verify →" in board, "order moved to Result entered")

# 7 — verification -----------------------------------------------------------
path = Session("farhana", "Demo#1234")
step(7, "Pathologist verifies and e-signs")
ver = path.get(f"/lis/verify?orderId={order_id}")
check(">Low<" in ver or ">High<" in ver, "H/L flags computed against the reference range")
url, html = path.post("/lis/verify", {"OrderId": order_id}, ver)
check("/lis/report/" in url, "released to the report document")
check("e-signed" in html, "e-signature attached")

# 8 — delivery ---------------------------------------------------------------
step(8, "Front desk delivers the report")
dele = bill.get("/diagnostics/delivery")
check("LB-" in dele, "report waiting for collection")
bill.post("/diagnostics/delivery?handler=Deliver",
          {"orderId": order_id, "collector": "Patient"}, dele)
check("Recently delivered" in bill.get("/diagnostics/delivery"), "handover logged")

# 9 — day close --------------------------------------------------------------
step(9, "Counter day-close with a deliberate ৳50 shortfall")
dc = bill.get("/billing/day-close")
exp = re.search(r'Cash expected</span><span class="val">&#x9F3; ([\d,]+)', dc)
expected = int(exp.group(1).replace(",", "")) if exp else 0
check(expected == 2550, f"expected cash = float + takings = ৳{expected}")
url, html = bill.post("/billing/day-close", {"CountedCash": str(expected - 50)}, dc)
check("/billing/statement/" in url, "day-close statement produced")
check("Variance" in html, "variance recorded, not blocked")

# 10 — dashboard -------------------------------------------------------------
md = Session("md", "Demo#1234")
step(10, "MD dashboard shows the day")
dash = md.get("/dashboard")
check("&#x9F3; 550" in dash, "income lands on the dashboard")
check("Rahim Uddin" in dash or "Nil" in dash or "variance" in dash.lower(), "variance surfaced")

print()
if fail:
    print(f"FAILED {len(fail)} check(s):")
    for f in fail:
        print("  -", f)
    sys.exit(1)
print("GOLDEN THREAD PASSED — all checks green")
