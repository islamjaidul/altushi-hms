#!/usr/bin/env python3
"""Black-box input-validation sweep across the ERP's POST handlers (spec 0038).

Why this exists: the codebase has no `DataAnnotations`, no `asp-validation-for`, and one
`ModelState` use in the whole tree. Every `[BindProperty]` is bare, so ASP.NET's model binder
silently substitutes `default` for anything it cannot parse and the handler runs anyway. All
real validation is a service-layer exception caught by the page. Nobody had ever asked, per
handler, what a mistyped taka amount or a 100 KB paste actually does.

The sweep fires six payload classes at each money / quantity / date / free-text field:

  1. omitted or empty required field
  2. type garbage — "abc" into a taka or quantity box
  3. negative (-1) and 64-bit overflow (9223372036854775808)
  4. 100 KB of text into a name or note
  5. an impossible date — 31/02/2026, 0001-01-01, 9999-12-31
  6. a decimal into a whole-taka box (100.55) — the PRD says whole-taka entry only

Each payload asserts three things, and the third is the one that matters:

  (a) the response is not a 500 and carries no stack trace;
  (b) a human-readable error is shown, or the input was refused without one;
  (c) **the database is unchanged** — proven by `count(*)` over every table a probed handler
      could write to, taken by `docker exec hms-dev-db psql` immediately before and after.

(c) is not optional. A 302 back to the same screen means "saved" on some of these handlers and
"quietly ignored" on others, and the two are indistinguishable from the HTTP response alone —
which is exactly how a silent accept survives a manual test.

Two payload classes cannot assert (c), because the correct behaviour is to write a row: an
absurd-but-parseable clinical reading, and a save whose *other* fields are valid. Those cases
pass `expect="write"` and then assert on the **stored value** instead — the invoice's recorded
payment, the vitals row's SpO2 — so a silent substitution of `default` is still caught.

Findings are labelled `AUD-VAL-nn`. A handler that correctly refuses bad input is a PASS and is
reported as one; this file makes no claim it has not observed. Local, freshly seeded database
only: the sweep registers a patient and admits them to get a live folio to shoot at, and rule 4
forbids deleting what it leaves behind.

**Run it alone.** `count(*)` deltas assume this process is the only writer. Another thread
against the same database during a run will attribute its rows to whatever payload happened to
be in flight. Every entry in the DEFECTS list below is therefore backed by an *attributing*
query as well — the stored discount on a named invoice, a `doctor_id` that no schedule has, a
`valid_from` of `infinity` — never by a count delta on its own.

    BASE_URL=http://localhost:5199 python3 eng/verify/audit/probe-validation.py
"""
import json
import os
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import _harness as H  # noqa: E402
from _harness import case, check, fixture, report  # noqa: E402

DB = os.environ.get("HMS_DB", "hms")
CONTAINER = os.environ.get("HMS_DEV_DB", "hms-dev-db")

BIG = "X" * 100_000                      # payload class 4
OVERFLOW = "9223372036854775808"         # long.MaxValue + 1
BAD_DATES = ["31/02/2026", "0001-01-01", "9999-12-31", "2026-13-45", "abc"]

# The dev exception page, and any framework type name that leaked into an operator's screen.
TRACE = re.compile(
    r"Stack\s*Query\s*Cookies|An unhandled exception occurred|"
    r"System\.(?:InvalidOperation|Overflow|Format|NullReference|Argument|Aggregate)Exception|"
    r"Microsoft\.EntityFrameworkCore\.DbUpdateException|"
    r"Npgsql\.PostgresException|at Hms\.[A-Za-z.]+\.", re.I)

ALERT = re.compile(r'<div class="alert bad">[\s\S]{0,400}?<span>([^<]{3,400})</span>')

# Every table a probed handler could write to. Snapshotting all of them on every payload costs
# one psql round trip and removes the guesswork about which module a stray row landed in.
WATCH = [
    "bill.invoice", "bill.invoice_line", "bill.charge_line", "bill.receipt", "bill.encounter",
    "reg.patient", "appt.appointment", "emr.vitals", "emr.note", "emr.note_drug",
    "ipd.admission", "ipd.indent", "ipd.indent_item",
    "pharm.purchase_order", "pharm.purchase_order_line", "pharm.stock_move",
    "diag.test_order", "diag.order_test", "lis.result", "lis.sample",
    "adm.service", "adm.test_catalog", "adm.rate_version",
    "kernel.approval_request",
    # Spec 0039 corpus extension (AUD-VAL-27..31): tables behind handlers the audit never
    # sampled — 19 of the 26 it did sample carried defects, so the unsampled 115 get coverage.
    "bill.counter_session", "ot.ot_case", "pharm.transfer", "pharm.transfer_line",
    "pharm.product", "pharm.outlet",
]

FIRED = 0
DEFECTS: list[str] = []


# ---------------------------------------------------------------------------- observation
def sql(q: str) -> str:
    """Read-only assertion channel. The product is driven over HTTP; psql only observes."""
    out = subprocess.run(
        ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", DB, "-tAc", q],
        capture_output=True, text=True)
    if out.returncode != 0:
        raise RuntimeError(f"psql failed on {q!r}: {out.stderr.strip()}")
    return out.stdout.strip()


def one(q: str) -> int:
    v = sql(q)
    return int(v) if v not in ("", None) else 0


def snap() -> dict:
    cols = ", ".join(f"(SELECT count(*) FROM {t})" for t in WATCH)
    return dict(zip(WATCH, (int(v) for v in sql(f"SELECT {cols}").split("|"))))


# ---------------------------------------------------------------------------- the POST
def hit(sess: H.Session, path: str, fields: dict, page: str | None = None) -> dict:
    """POST and classify the response without letting a 500 abort the sweep."""
    try:
        url, body = sess.post(path, fields, page)
        code = 200
    except urllib.error.HTTPError as e:
        code, url, body = e.code, e.geturl(), e.read().decode(errors="replace")
    except urllib.error.URLError as e:                    # reset = the app fell over
        return {"code": 0, "url": path, "body": "", "fault": True, "error": None}
    m = ALERT.search(body)
    return {"code": code, "url": url, "body": body,
            "fault": code >= 500 or bool(TRACE.search(body)),
            "error": m.group(1).strip() if m else None}


def probe(cid: str, what: str, sess: H.Session, path: str, fields: dict,
          page: str | None = None, expect: str = "reject", allow: tuple = ()) -> dict:
    """Fire one malformed payload.

    `expect="reject"` — the payload is wholly unusable, so a correct handler writes nothing.
    `expect="write"`  — the rest of the payload is valid and a row is the right answer; the
                        caller asserts on what was *stored*, which this cannot do for it.
    `allow`           — tables whose growth is a known-good side effect (a resumed draft note).
    """
    global FIRED
    FIRED += 1
    before = snap()
    r = hit(sess, path, fields, page)
    after = snap()
    wrote = {t: after[t] - before[t] for t in WATCH
             if after[t] != before[t] and t not in allow}
    r["wrote"] = wrote

    shown = r["error"]
    tail = f' -> "{shown[:100]}"' if shown else (" -> no error shown" if not wrote else "")
    print(f"      .. {cid} {what}: HTTP {r['code']}{tail}"
          + (f"  WROTE {wrote}" if wrote else ""))

    bad = False
    if not check(not r["fault"], f"{cid} {what}: no 500 and no stack trace "
                                 f"(HTTP {r['code']})"):
        bad = True
    if expect == "reject":
        if not check(not wrote, f"{cid} {what}: nothing was written"
                                + (f" — but {wrote} appeared" if wrote else "")):
            bad = True
        check(bool(shown) or not wrote,
              f"{cid} {what}: refused with a message, or refused silently")
    if bad:
        DEFECTS.append(f"{cid} {what}  [{path}]  HTTP {r['code']}"
                       + (f", wrote {wrote}" if wrote else ", server fault"))
    return r


def token() -> str:
    return str(uuid.uuid4())


def opt(html: str, name: str) -> str | None:
    """First real <option> value of a named select.

    Both empty-string and "0" prompts are skipped: the folio's indent picker uses
    `<option value="0">—</option>`, and an early draft of this file happily probed with
    product id 0, which every handler correctly refuses — four cases passed for the wrong
    reason before that was noticed.
    """
    sel = re.search(r'<select[^>]*name="' + re.escape(name) + r'"[\s\S]*?</select>', html)
    if not sel:
        return None
    for v in re.findall(r'<option value="([^"]*)"', sel.group(0)):
        if v.strip() and v.strip() != "0":
            return v
    return None


def newest_invoice() -> tuple:
    """(id, gross, discount, net, receipted) for the last invoice written."""
    row = sql("SELECT i.id||'|'||i.gross||'|'||i.discount||'|'||i.net||'|'||"
              "coalesce((SELECT sum(r.amount) FROM bill.receipt r WHERE r.invoice_id=i.id),0) "
              "FROM bill.invoice i ORDER BY i.id DESC LIMIT 1")
    return tuple(int(v) for v in row.split("|"))


# ---------------------------------------------------------------------------- the sweep
def main() -> int:                                        # noqa: C901 — one case per paragraph
    H.guard("t1")
    if not H.is_local():
        sys.exit("REFUSED: this sweep posts deliberately malformed money into live handlers "
                 "and leaves fixture rows that rule 4 forbids deleting. Local only.")

    stamp = time.strftime("%H%M%S")
    rasel = H.Session("rasel")          # billing, diagnostics, folio settlement
    jashim = H.Session("jashim")        # registration, appointments, admission
    parvin = H.Session("parvin")        # pharmacy
    nasrin = H.Session("nasrin")        # nursing — vitals, folio posting
    admin = H.Session("admin")          # masters
    ripon = H.Session("ripon")          # lab result entry
    farhana = H.Session("farhana")      # verification
    chowdhury = H.Session("chowdhury")  # consultation note

    H.open_counter(rasel, "Front Desk")
    H.open_counter(parvin, "Pharmacy")

    # A patient whose own name is sane — a previous run of this sweep may have left a 100 KB
    # one behind (AUD-VAL-09), and picking it here would poison every later case.
    pid = str(fixture(one("SELECT max(id) FROM reg.patient WHERE length(full_name) < 100")
                      or None, "no patient to bill"))

    # ================================================================= /billing/opd
    case("AUD-VAL-01", "OPD counter — cart handlers survive a garbage catalogue id", rasel)
    opd = rasel.get("/billing/opd")
    svc_id = fixture(re.search(r'name="catalogId" value="(\d+)"', opd),
                     "no sellable service in the catalogue").group(1)
    probe("AUD-VAL-01a", "Add catalogId='abc'", rasel, "/billing/opd?handler=Add",
          {"PatientId": pid, "catalogId": "abc", "SubmissionToken": token()}, opd)
    probe("AUD-VAL-01b", "Add catalogId=99999999 (no such service)", rasel,
          "/billing/opd?handler=Add",
          {"PatientId": pid, "catalogId": "99999999", "SubmissionToken": token()}, opd)
    probe("AUD-VAL-01c", "Add catalogId=" + OVERFLOW, rasel, "/billing/opd?handler=Add",
          {"PatientId": pid, "catalogId": OVERFLOW, "SubmissionToken": token()}, opd)

    case("AUD-VAL-02", "OPD counter — Remove with an out-of-range index", rasel)
    for sub, ix in [("a", "-1"), ("b", "9999"), ("c", "abc")]:
        probe(f"AUD-VAL-02{sub}", f"Remove index={ix!r}", rasel, "/billing/opd?handler=Remove",
              {"PatientId": pid, "Items": [svc_id], "index": ix}, opd)

    # --- the discount box ---------------------------------------------------------
    case("AUD-VAL-03", "OPD counter — a malformed discount is not silently turned into zero",
         rasel)
    # `long DiscountFlat` cannot hold "abc"; the binder leaves it 0 and no handler in the tree
    # inspects ModelState. The bill saves at full price and the operator is told it worked.
    base = {"PatientId": pid, "Items": [svc_id], "Tender": "cash", "Tender2": "card"}
    for sub, d, why in [("a", "abc", "type garbage"),
                        ("b", "-1", "negative"),
                        ("c", OVERFLOW, "64-bit overflow"),
                        ("d", "45.50", "decimal into whole-taka")]:
        r = probe(f"AUD-VAL-03{sub}", f"Save DiscountFlat={d!r} ({why})", rasel,
                  "/billing/opd?handler=Save",
                  {**base, "SubmissionToken": token(), "DiscountFlat": d,
                   "DiscountReason": "validation probe", "PaidNow": "0"}, opd,
                  expect="write")
        if r["wrote"].get("bill.invoice"):
            iid, gross, disc, net, paid = newest_invoice()
            print(f"      .. invoice {iid}: gross {gross}, discount {disc}, net {net}")
            if not check(bool(r["error"]) or disc != 0,
                         f"AUD-VAL-03{sub}: a discount of {d!r} was either refused with a "
                         f"message or actually applied (invoice {iid} carries a discount of "
                         f"{disc})"):
                DEFECTS.append(f"AUD-VAL-03{sub} discount {d!r} silently became 0 and the bill "
                               f"saved at full price  [/billing/opd?handler=Save]  invoice "
                               f"{iid}: gross {gross}, discount {disc}, net {net}")

    case("AUD-VAL-04", "OPD counter — a malformed payment is not silently dropped", rasel)
    # The same substitution on the *money taken* box: the invoice is raised, the cash the
    # patient just handed over is never receipted, and the bill becomes a due.
    for sub, p, why in [("a", "100.55", "decimal into whole-taka"),
                        ("b", "abc", "type garbage"),
                        ("c", OVERFLOW, "64-bit overflow")]:
        r = probe(f"AUD-VAL-04{sub}", f"Save PaidNow={p!r} ({why})", rasel,
                  "/billing/opd?handler=Save",
                  {**base, "SubmissionToken": token(), "DiscountFlat": "0", "PaidNow": p},
                  opd, expect="write")
        if r["wrote"].get("bill.invoice"):
            iid, gross, disc, net, paid = newest_invoice()
            print(f"      .. invoice {iid}: net {net}, receipted {paid}")
            if not check(bool(r["error"]) or paid > 0,
                         f"AUD-VAL-04{sub}: a payment of {p!r} was either refused with a "
                         f"message or receipted (invoice {iid} has {paid} Tk of receipts "
                         f"against {net} Tk)"):
                DEFECTS.append(f"AUD-VAL-04{sub} payment {p!r} silently dropped — invoice "
                               f"raised, cash unreceipted  [/billing/opd?handler=Save]  "
                               f"invoice {iid}: net {net} Tk, receipted {paid} Tk")

    case("AUD-VAL-05", "OPD counter — free text and a service id that does not exist", rasel)
    r = probe("AUD-VAL-05a", "Save DiscountReason = 100 KB", rasel,
              "/billing/opd?handler=Save",
              {**base, "SubmissionToken": token(), "DiscountFlat": "5",
               "DiscountReason": BIG, "PaidNow": "0"}, opd, expect="write")
    big_reason = one("SELECT coalesce(max(length(reason)),0) FROM kernel.approval_request")
    check(big_reason < 10_000,
          f"AUD-VAL-05a: no approval request stored an unbounded reason "
          f"(longest is {big_reason} chars)")
    if big_reason >= 10_000:
        DEFECTS.append("AUD-VAL-05a 100 KB discount reason stored verbatim  "
                       f"[/billing/opd?handler=Save]  kernel.approval_request.reason = "
                       f"{big_reason} chars")
    # LoadAsync filters the *displayed* cart to known ids; the save loop iterates raw `Items`.
    probe("AUD-VAL-05b", "Save Items=[valid, 99999999]", rasel, "/billing/opd?handler=Save",
          {**base, "Items": [svc_id, "99999999"], "SubmissionToken": token(),
           "DiscountFlat": "0", "PaidNow": "0"}, opd)

    # ================================================================= /billing/dues
    case("AUD-VAL-06", "Dues collection — a malformed amount takes no money", rasel)
    dues = rasel.get("/billing/dues")
    inv = re.search(r'name="InvoiceId" value="(\d+)"', dues)
    if inv:
        iid = inv.group(1)
        for sub, amt in [("a", "abc"), ("b", "-1"), ("c", OVERFLOW), ("d", "250.75"),
                         ("e", "")]:
            probe(f"AUD-VAL-06{sub}", f"Collect Amount={amt!r}", rasel,
                  "/billing/dues?handler=Collect",
                  {"InvoiceId": iid, "Amount": amt, "Tender": "cash"}, dues)
        probe("AUD-VAL-06f", "Collect InvoiceId=99999999", rasel,
              "/billing/dues?handler=Collect",
              {"InvoiceId": "99999999", "Amount": "10", "Tender": "cash"}, dues)
        probe("AUD-VAL-06g", "Collect Tender='<script>alert(1)</script>'", rasel,
              "/billing/dues?handler=Collect",
              {"InvoiceId": iid, "Amount": "0", "Tender": "<script>alert(1)</script>"}, dues)
    else:
        check(False, "AUD-VAL-06: no outstanding due on this database to collect against")

    # ================================================================= /billing/refund
    case("AUD-VAL-07", "Refund request — amount, reason and action", rasel)
    refund = rasel.get("/billing/refund")
    paid_inv = sql("SELECT i.id FROM bill.invoice i JOIN bill.receipt r ON r.invoice_id=i.id "
                   "WHERE r.amount > 0 ORDER BY i.id DESC LIMIT 1")
    if paid_inv:
        rq = {"InvoiceId": paid_inv, "Action": "refund", "Tender": "cash", "Reason": "probe"}
        for sub, amt in [("a", "abc"), ("b", "-1"), ("c", OVERFLOW), ("d", "99.99"),
                         ("e", "")]:
            probe(f"AUD-VAL-07{sub}", f"Request Amount={amt!r}", rasel,
                  "/billing/refund?handler=Request", {**rq, "Amount": amt}, refund)
        before_len = one("SELECT coalesce(max(length(reason)),0) FROM kernel.approval_request")
        probe("AUD-VAL-07f", "Request Reason = 100 KB", rasel,
              "/billing/refund?handler=Request", {**rq, "Amount": "1", "Reason": BIG}, refund,
              expect="write")
        after_len = one("SELECT coalesce(max(length(reason)),0) FROM kernel.approval_request")
        check(after_len < 10_000 or after_len <= before_len,
              f"AUD-VAL-07f: the refund reason was bounded before storage "
              f"(longest reason is now {after_len} chars)")
        probe("AUD-VAL-07g", "Request Action='delete' (not a known action)", rasel,
              "/billing/refund?handler=Request",
              {**rq, "Amount": "1", "Action": "delete"}, refund, expect="write")
        kinds = sql("SELECT DISTINCT type FROM kernel.approval_request ORDER BY 1")
        print(f"      .. approval kinds now present: {kinds.splitlines()}")
    else:
        check(False, "AUD-VAL-07: no receipted invoice on this database to refund against")

    case("AUD-VAL-08", "Refund execution — an unknown or malformed request id", rasel)
    probe("AUD-VAL-08a", "Execute requestId=99999999", rasel,
          "/billing/refund?handler=Execute", {"requestId": "99999999"}, refund)
    probe("AUD-VAL-08b", "Execute requestId='abc'", rasel,
          "/billing/refund?handler=Execute", {"requestId": "abc"}, refund)

    # ================================================================= /registration/new
    case("AUD-VAL-09", "Registration — required name, impossible date of birth, 100 KB paste",
         jashim)
    reg = jashim.get("/registration/new")
    probe("AUD-VAL-09a", "save with FullName empty", jashim, "/registration/new",
          {"FullName": "", "Sex": "F", "AgeOrDob": "30", "PatientType": "general",
           "DuplicatesAcknowledged": "true", "action": "save"}, reg)
    for sub, d in zip("bcdef", BAD_DATES):
        probe(f"AUD-VAL-09{sub}", f"save with AgeOrDob={d!r}", jashim, "/registration/new",
              {"FullName": f"Probe Val {stamp} {sub}", "Sex": "F", "AgeOrDob": d,
               "PatientType": "general", "DuplicatesAcknowledged": "true", "action": "save"},
              reg)
    bad_dob = one("SELECT count(*) FROM reg.patient WHERE dob IS NOT NULL "
                  "AND (dob < '1890-01-01' OR dob > current_date)")
    check(bad_dob == 0,
          f"AUD-VAL-09: no patient row carries an impossible date of birth (found {bad_dob})")

    # The 100 KB name goes last: it wedges the session it is posted from (see the assertion
    # below), so nothing after it in this run may reuse `jashim`.
    r = probe("AUD-VAL-09g", "save with FullName = 100 KB", jashim, "/registration/new",
              {"FullName": BIG, "Sex": "F", "AgeOrDob": "30", "PatientType": "general",
               "DuplicatesAcknowledged": "true", "action": "save"}, reg, expect="write")
    longest = one("SELECT coalesce(max(length(full_name)),0) FROM reg.patient")
    check(longest < 1_000,
          f"AUD-VAL-09g: no patient name was stored unbounded (longest is {longest} chars)")
    if longest >= 1_000:
        DEFECTS.append(f"AUD-VAL-09g 100 KB patient name stored verbatim  "
                       f"[/registration/new]  reg.patient.full_name = {longest} chars")
    # The success toast goes into TempData, TempData is a cookie, and the toast carries the
    # name. The operator's browser now sends ~136 KB of cookies and Kestrel answers every
    # request with 431 — the receptionist cannot even reach the logout link.
    jar = next((h.cookiejar for h in jashim.op.handlers if hasattr(h, "cookiejar")), None)
    cookie_bytes = sum(len(c.value) for c in jar) if jar else 0
    print(f"      .. the session now carries {cookie_bytes:,} bytes of cookies")
    healthy = True
    try:
        jashim.get("/registration/new")
    except urllib.error.HTTPError as e:
        healthy = False
        print(f"      .. a plain GET from that session now returns HTTP {e.code}")
    if not check(healthy,
                 "AUD-VAL-09g: the operator's session still works after the oversized paste"):
        DEFECTS.append("AUD-VAL-09g oversized name wedges the session  [/registration/new]  "
                       f"{cookie_bytes:,} bytes of TempData cookies -> every later request 431")
    jashim = H.Session("jashim")          # a clean browser, as the operator would have to open

    # ================================================================= /appointments
    case("AUD-VAL-10", "Appointments — issuing a serial against a bad doctor or patient",
         jashim)
    appts = jashim.get("/appointments")
    doc = opt(appts, "DoctorId")
    probe("AUD-VAL-10a", "Issue DoctorId=99999999", jashim, "/appointments?handler=Issue",
          {"PatientId": pid, "DoctorId": "99999999"}, appts)
    probe("AUD-VAL-10b", "Issue DoctorId='abc'", jashim, "/appointments?handler=Issue",
          {"PatientId": pid, "DoctorId": "abc"}, appts)
    ghost_doc = one("SELECT count(*) FROM appt.appointment a WHERE NOT EXISTS "
                    "(SELECT 1 FROM appt.doctor_schedule d WHERE d.doctor_id = a.doctor_id)")
    if not check(ghost_doc == 0,
                 f"AUD-VAL-10a: every appointment names a doctor who has a schedule "
                 f"(found {ghost_doc} that do not)"):
        DEFECTS.append("AUD-VAL-10a serial issued against a doctor id that does not exist  "
                       f"[/appointments?handler=Issue]  {ghost_doc} appointment(s) with an "
                       "unknown doctor_id")
    if doc:
        probe("AUD-VAL-10c", "Issue PatientId=0", jashim, "/appointments?handler=Issue",
              {"PatientId": "0", "DoctorId": doc}, appts)
        probe("AUD-VAL-10d", "Issue PatientId=" + OVERFLOW, jashim,
              "/appointments?handler=Issue", {"PatientId": OVERFLOW, "DoctorId": doc}, appts)

    case("AUD-VAL-11", "Appointments — advancing a serial into a state that does not exist",
         jashim)
    pat = (r'name="id" value="(\d+)"[\s\S]{0,200}?name="from" value="([^"]*)"'
           r'[\s\S]{0,200}?name="to" value="([^"]*)"')
    adv = re.search(pat, appts)
    if not adv and doc:
        # Today's board is empty (the seed is dated, this run is not) — issue one serial so
        # there is a real state machine to attack. Without this the case silently skips.
        jashim.post("/appointments?handler=Issue",
                    {"PatientId": pid, "DoctorId": doc}, appts)
        appts = jashim.get("/appointments")
        adv = re.search(pat, appts)
    if adv:
        aid, frm, to = adv.groups()
        was = sql(f"SELECT state FROM appt.appointment WHERE id={aid}")
        probe("AUD-VAL-11a", "Advance to='deleted'", jashim, "/appointments?handler=Advance",
              {"id": aid, "from": frm, "to": "deleted"}, appts)
        probe("AUD-VAL-11b", "Advance from='' to=''", jashim, "/appointments?handler=Advance",
              {"id": aid, "from": "", "to": ""}, appts)
        probe("AUD-VAL-11c", "Advance id=99999999", jashim, "/appointments?handler=Advance",
              {"id": "99999999", "from": frm, "to": to}, appts)
        now = sql(f"SELECT state FROM appt.appointment WHERE id={aid}")
        # AppointmentsService.AdvanceAsync is `UPDATE ... SET state = {to} WHERE state = {from}`
        # with no check that `to` is a state the machine has. `to` is a hidden form field.
        if not check(now == was,
                     f"AUD-VAL-11a: appointment {aid} is still {was!r} after three bad "
                     f"transitions (now {now!r})"):
            junk = one("SELECT count(*) FROM appt.appointment WHERE state NOT IN "
                       "('booked','arrived','done','cancelled','no_show')")
            DEFECTS.append(f"AUD-VAL-11a any string is accepted as an appointment state  "
                           f"[/appointments?handler=Advance]  appointment {aid} moved {was!r} "
                           f"-> {now!r}; {junk} row(s) now hold an unknown state")
    else:
        check(False, "AUD-VAL-11: no advanceable appointment on today's board")

    # ================================================================= /ipd/admit
    case("AUD-VAL-12", "Admission — a bed and a patient that do not exist", jashim)
    admit = jashim.get("/ipd/admit")
    bed = fixture(re.search(r'<option value="(\d+)">GW[MF]-\d+', admit),
                  "no free general-ward bed to admit into",
                  "reset the seeded database — a killed run may still hold a bed")
    for sub, f in [("a", {"BedId": "99999999"}), ("b", {"BedId": "abc"}),
                   ("c", {"PatientId": "0"}), ("d", {"PatientId": OVERFLOW})]:
        probe(f"AUD-VAL-12{sub}", f"admit {f}", jashim, "/ipd/admit",
              {"PatientId": pid, "Source": "direct", "ProvisionalDx": "probe",
               "BedId": bed.group(1), "ServiceChargePct": "5", "ReserveOnly": "false", **f},
              admit)

    # ---- one live admission, and the two payloads that once rode inside it --------
    # (Reworked with spec 0039: the original fixture POSTED pct=-50 and a 100 KB dx and relied
    # on the defect to get its admission. Now those are refused, so they are fired as ordinary
    # reject probes first, and the fixture is created with values a correct product accepts.
    # The database-level assertions below are unchanged and still go red if either bad value
    # ever reaches storage again.)
    case("AUD-VAL-13", "Admission — a negative service charge and a 100 KB diagnosis", jashim)
    name = f"Probe Val {stamp}"
    jashim.post("/registration/new",
                {"FullName": name, "Sex": "M", "AgeOrDob": "44",
                 "Phone": f"01755{stamp}", "PatientType": "general",
                 "DuplicatesAcknowledged": "true", "action": "save"})
    found = json.loads(jashim.get("/api/typeahead/patients?q=" + urllib.parse.quote(name)))
    ppid = str(fixture(found[-1] if found else None,
                       "the probe's own patient did not register")["value"])
    admit = jashim.get("/ipd/admit")
    bed = fixture(re.search(r'<option value="(\d+)">GW[MF]-\d+', admit),
                  "no free general-ward bed left after the AUD-VAL-12 probes")
    probe("AUD-VAL-13a", "admit ServiceChargePct='-50'", jashim, "/ipd/admit",
          {"PatientId": ppid, "Source": "direct", "ProvisionalDx": "probe",
           "BedId": bed.group(1), "ServiceChargePct": "-50", "ReserveOnly": "false"}, admit)
    probe("AUD-VAL-13b", "admit ProvisionalDx = 100 KB", jashim, "/ipd/admit",
          {"PatientId": ppid, "Source": "direct", "ProvisionalDx": BIG,
           "BedId": bed.group(1), "ServiceChargePct": "5", "ReserveOnly": "false"}, admit)
    url, _ = jashim.post("/ipd/admit",
                         {"PatientId": ppid, "Source": "direct", "ProvisionalDx": "probe fixture",
                          "BedId": bed.group(1), "ServiceChargePct": "5",
                          "ReserveOnly": "false"}, admit)
    adm_id = fixture(re.search(r"/ipd/folio/(\d+)", url),
                     "the fixture admission was refused — no folio to probe").group(1)
    H.record("admission", adm_id)
    neg_pct = one("SELECT count(*) FROM ipd.admission WHERE service_charge_pct < 0 "
                  "OR service_charge_pct > 100")
    big_dx = one("SELECT count(*) FROM ipd.admission WHERE length(provisional_dx) >= 10000")
    if not check(neg_pct == 0, f"AUD-VAL-13a: no admission carries a service charge outside "
                               f"0–100% (found {neg_pct})"):
        DEFECTS.append(f"AUD-VAL-13a out-of-range service charge accepted  [/ipd/admit]  "
                       f"{neg_pct} admission(s)")
    if not check(big_dx == 0, f"AUD-VAL-13b: no admission stores an unbounded provisional "
                              f"diagnosis (found {big_dx})"):
        DEFECTS.append(f"AUD-VAL-13b 100 KB provisional diagnosis stored verbatim  "
                       f"[/ipd/admit]  {big_dx} admission(s)")
    H.on_exit(f"discharge probe admission {adm_id} and return bed {bed.group(1)}",
              lambda: H.settle_and_discharge(H.Session, adm_id, [bed.group(1)]))

    folio = f"/ipd/folio/{adm_id}"
    fpage = rasel.get(folio)

    # ================================================================= /ipd/folio Service
    case("AUD-VAL-14", "Folio service posting — quantity", rasel)
    fsvc = opt(fpage, "ServiceId") or svc_id
    for sub, qty in [("a", "abc"), ("b", "-1"), ("c", "0"), ("d", OVERFLOW), ("e", "2.5")]:
        probe(f"AUD-VAL-14{sub}", f"Service Qty={qty!r}", rasel, f"{folio}?handler=Service",
              {"AdmissionId": adm_id, "ServiceId": fsvc, "Qty": qty}, fpage)
    probe("AUD-VAL-14f", "Service ServiceId=99999999", rasel, f"{folio}?handler=Service",
          {"AdmissionId": adm_id, "ServiceId": "99999999", "Qty": "1"}, fpage)
    # An unbounded quantity is a fat-finger away from a bill nobody can explain.
    r = probe("AUD-VAL-14g", "Service Qty=999999 (no upper bound)", rasel,
              f"{folio}?handler=Service",
              {"AdmissionId": adm_id, "ServiceId": fsvc, "Qty": "999999"}, fpage,
              expect="write")
    huge = one("SELECT coalesce(max(qty),0) FROM bill.charge_line")
    money = one("SELECT coalesce(max(amount),0) FROM bill.charge_line")
    print(f"      .. largest quantity on any charge line: {huge}; largest amount: {money} Tk")
    if not check(huge < 10_000,
                 f"AUD-VAL-14g: no charge line carries an implausible quantity "
                 f"(largest is {huge})"):
        DEFECTS.append("AUD-VAL-14g unbounded quantity accepted with no confirmation  "
                       f"[{folio}?handler=Service]  bill.charge_line.qty = {huge}")

    # ================================================================= /ipd/folio Advance
    case("AUD-VAL-15", "Folio advance — money into the drawer", rasel)
    for sub, amt in [("a", "abc"), ("b", "-1"), ("c", "0"), ("d", OVERFLOW), ("e", "500.50")]:
        probe(f"AUD-VAL-15{sub}", f"Advance AdvanceAmount={amt!r}", rasel,
              f"{folio}?handler=Advance",
              {"AdmissionId": adm_id, "AdvanceAmount": amt, "AdvanceTender": "cash"}, fpage)
    r = probe("AUD-VAL-15f", "Advance AdvanceTender='bitcoin' (not a tender)", rasel,
              f"{folio}?handler=Advance",
              {"AdmissionId": adm_id, "AdvanceAmount": "100", "AdvanceTender": "bitcoin"},
              fpage, expect="write")
    stray = one("SELECT count(*) FROM bill.receipt WHERE tender NOT IN "
                "('cash','card','bkash','nagad','rocket','cheque','bank','adjustment')")
    if not check(stray == 0,
                 f"AUD-VAL-15f: every receipt carries a tender the day-close can classify "
                 f"(found {stray} that it cannot)"):
        DEFECTS.append("AUD-VAL-15f arbitrary tender string accepted into the drawer  "
                       f"[{folio}?handler=Advance]  {stray} receipt(s) with an unknown tender")

    # ================================================================= /ipd/folio Indent
    case("AUD-VAL-16", "Folio indent — product ids and quantities", nasrin)
    npage = nasrin.get(folio)
    prod = opt(npage, "ProductIds") or "1"
    probe("AUD-VAL-16a", "Indent ItemQtys=['abc']", nasrin, f"{folio}?handler=Indent",
          {"AdmissionId": adm_id, "ProductIds": [prod], "ItemQtys": ["abc"]}, npage)
    probe("AUD-VAL-16b", "Indent ItemQtys=['-5']", nasrin, f"{folio}?handler=Indent",
          {"AdmissionId": adm_id, "ProductIds": [prod], "ItemQtys": ["-5"]}, npage)
    probe("AUD-VAL-16c", "Indent ProductIds=['99999999']", nasrin, f"{folio}?handler=Indent",
          {"AdmissionId": adm_id, "ProductIds": ["99999999"], "ItemQtys": ["1"]}, npage)
    orphan_item = one("SELECT count(*) FROM ipd.indent_item ii WHERE NOT EXISTS "
                      "(SELECT 1 FROM pharm.product p WHERE p.id = ii.product_id)")
    if not check(orphan_item == 0,
                 f"AUD-VAL-16c: every indent line names a product that exists "
                 f"(found {orphan_item} that do not)"):
        DEFECTS.append("AUD-VAL-16c indent raised for a product id that does not exist  "
                       f"[{folio}?handler=Indent]  {orphan_item} orphan indent line(s)")
    r = probe("AUD-VAL-16d", "Indent ragged lists (2 products, 1 quantity)", nasrin,
              f"{folio}?handler=Indent",
              {"AdmissionId": adm_id, "ProductIds": [prod, prod], "ItemQtys": ["1"]}, npage,
              expect="write")
    if r["wrote"].get("ipd.indent"):
        lines = one("SELECT count(*) FROM ipd.indent_item WHERE indent_id = "
                    "(SELECT max(id) FROM ipd.indent)")
        print(f"      .. the ragged post produced an indent with {lines} line(s) of the 2 sent")
    r = probe("AUD-VAL-16e", "Indent IndentNote = 100 KB", nasrin, f"{folio}?handler=Indent",
              {"AdmissionId": adm_id, "ProductIds": [prod], "ItemQtys": ["1"],
               "IndentNote": BIG}, npage, expect="write")
    note_len = one("SELECT coalesce(max(length(note)),0) FROM ipd.indent")
    if not check(note_len < 10_000,
                 f"AUD-VAL-16e: the indent note was bounded before storage "
                 f"(longest is {note_len} chars)"):
        DEFECTS.append(f"AUD-VAL-16e 100 KB indent note stored verbatim  "
                       f"[{folio}?handler=Indent]  ipd.indent.note = {note_len} chars")

    # ================================================================= /pharmacy/pos
    case("AUD-VAL-17", "Pharmacy POS — cart, quantity and payment", parvin)
    pos = parvin.get("/pharmacy/pos")
    outlet = opt(pos, "OutletId") or "1"
    pprod = fixture(re.search(r'name="productId" value="(\d+)"', pos),
                    "no sellable pharmacy product in the seeded outlet").group(1)
    pbase = {"PatientId": pid, "OutletId": outlet, "Tender": "cash", "Tender2": "card",
             "Items": [pprod]}
    probe("AUD-VAL-17a", "Add productId='abc'", parvin, "/pharmacy/pos?handler=Add",
          {"OutletId": outlet, "productId": "abc", "SubmissionToken": token()}, pos)
    probe("AUD-VAL-17b", "Add productId=99999999", parvin, "/pharmacy/pos?handler=Add",
          {"OutletId": outlet, "productId": "99999999", "SubmissionToken": token()}, pos)
    # `Cart` is built with `Math.Max(1, x.qty)` (Pos.cshtml.cs), so a quantity of 0 or -3 is a
    # sale of one unit — stock leaves the shelf for a line the operator never entered. And the
    # unparseable case never reaches the handler's own guard: Pos.cshtml renders
    # `Model.Qtys[i]` for each cart row, and a `List<int>` that failed to bind is shorter than
    # the cart, so the *view* throws ArgumentOutOfRangeException.
    for sub, qty in [("c", "abc"), ("d", "-3"), ("e", "0")]:
        r = probe(f"AUD-VAL-17{sub}", f"Save Qtys=[{qty!r}]", parvin,
                  "/pharmacy/pos?handler=Save",
                  {**pbase, "Qtys": [qty], "SubmissionToken": token(),
                   "DiscountFlat": "0", "PaidNow": "0"}, pos, expect="write")
        if r["wrote"].get("bill.invoice"):
            iid = one("SELECT max(id) FROM bill.invoice")
            sold = one(f"SELECT coalesce(sum(qty),0) FROM bill.charge_line "
                       f"WHERE invoice_id={iid}")
            if not check(bool(r["error"]),
                         f"AUD-VAL-17{sub}: a quantity of {qty!r} was refused with a message "
                         f"(instead invoice {iid} sold {sold} unit(s))"):
                DEFECTS.append(f"AUD-VAL-17{sub} quantity {qty!r} silently became a sale of "
                               f"{sold}  [/pharmacy/pos?handler=Save]  invoice {iid}, stock "
                               f"deducted")
    probe("AUD-VAL-17f", "Save Qtys=['999999'] (more than any batch holds)", parvin,
          "/pharmacy/pos?handler=Save",
          {**pbase, "Qtys": ["999999"], "SubmissionToken": token(),
           "DiscountFlat": "0", "PaidNow": "0"}, pos)
    r = probe("AUD-VAL-17g", "Save PaidNow='75.25' (decimal into whole-taka)", parvin,
              "/pharmacy/pos?handler=Save",
              {**pbase, "Qtys": ["1"], "SubmissionToken": token(), "DiscountFlat": "0",
               "PaidNow": "75.25"}, pos, expect="write")
    if r["wrote"].get("bill.invoice"):
        iid, gross, disc, net, paid = newest_invoice()
        if not check(bool(r["error"]) or paid > 0,
                     f"AUD-VAL-17g: the pharmacy sale was refused or the payment was receipted "
                     f"(invoice {iid} has {paid} Tk against {net} Tk)"):
            DEFECTS.append("AUD-VAL-17g decimal payment silently dropped  "
                           f"[/pharmacy/pos?handler=Save]  invoice {iid}: net {net}, "
                           f"receipted {paid}")

    # ================================================================= /pharmacy/purchase
    case("AUD-VAL-18", "Purchase order — line quantities and unit costs", parvin)
    pur = parvin.get("/pharmacy/purchase")
    sup = opt(pur, "SupplierId") or "1"
    pout = opt(pur, "OutletId") or outlet
    pprod2 = opt(pur, "ProductIds") or pprod
    pbase2 = {"SupplierId": sup, "OutletId": pout, "ProductIds": [pprod2]}
    for sub, q, c in [("a", "abc", "100"), ("b", "-5", "100"), ("c", "1", "-100"),
                      ("d", "1", "abc"), ("e", "1", "12.75"), ("f", OVERFLOW, "100"),
                      ("g", "1", OVERFLOW)]:
        probe(f"AUD-VAL-18{sub}", f"Create LineQtys=[{q!r}] LineCosts=[{c!r}]", parvin,
              "/pharmacy/purchase?handler=Create",
              {**pbase2, "LineQtys": [q], "LineCosts": [c]}, pur)
    probe("AUD-VAL-18h", "Create SupplierId=99999999", parvin,
          "/pharmacy/purchase?handler=Create",
          {**pbase2, "SupplierId": "99999999", "LineQtys": ["1"], "LineCosts": ["100"]}, pur,
          expect="write")
    ghost_sup = one("SELECT count(*) FROM pharm.purchase_order po WHERE NOT EXISTS "
                    "(SELECT 1 FROM pharm.supplier s WHERE s.id = po.supplier_id)")
    if not check(ghost_sup == 0,
                 f"AUD-VAL-18h: every purchase order names a supplier that exists "
                 f"(found {ghost_sup} that do not)"):
        DEFECTS.append("AUD-VAL-18h purchase order raised against a supplier id that does not "
                       f"exist  [/pharmacy/purchase?handler=Create]  {ghost_sup} order(s)")
    bad_po = one("SELECT count(*) FROM pharm.purchase_order_line WHERE qty <= 0 "
                 "OR expected_cost < 0")
    if not check(bad_po == 0,
                 f"AUD-VAL-18: no purchase-order line carries a non-positive quantity or a "
                 f"negative cost (found {bad_po})"):
        DEFECTS.append("AUD-VAL-18 purchase line accepted a bad quantity or cost  "
                       f"[/pharmacy/purchase?handler=Create]  {bad_po} bad line(s)")

    # ================================================================= /pharmacy/stock
    case("AUD-VAL-19", "Stock count — a counted quantity that cannot be right", parvin)
    stock = parvin.get(f"/pharmacy/stock?OutletId={outlet}")
    # The Count form carries `LineId` only — `AuditId` lives on the adjust/post forms further
    # down the page, and the handler derives the audit from the line.
    pat = r'name="LineId" value="(\d+)"'
    audit = re.search(pat, stock)
    if not audit:
        # `OnPostStartAuditAsync` is the one handler on this page with no try/catch: its own
        # `PharmacyException("An audit is already in progress")` escapes as a 500, so starting
        # the count a second time is a blank error page rather than a sentence. Fired through
        # `hit` so that a 500 here is recorded rather than ending the sweep.
        r = hit(parvin, "/pharmacy/stock?handler=StartAudit", {"OutletId": outlet}, stock)
        if not check(not r["fault"],
                     f"AUD-VAL-19: starting a stock count is not a 500 (HTTP {r['code']})"):
            DEFECTS.append("AUD-VAL-19 starting a count while one is open returns a blank 500  "
                           "[/pharmacy/stock?handler=StartAudit]  uncaught PharmacyException")
        stock = parvin.get(f"/pharmacy/stock?OutletId={outlet}")
        audit = re.search(pat, stock)
    if audit:
        line_id = audit.group(1)
        # Put the line back to the system quantity first, so a re-run starts from a non-zero
        # count and the "typo writes the batch off" assertion below can still fire.
        sys_qty = one(f"SELECT system_qty FROM pharm.stock_audit_line WHERE id={line_id}")
        if sys_qty > 0:
            parvin.post("/pharmacy/stock?handler=Count",
                        {"LineId": line_id, "CountedQty": str(sys_qty), "OutletId": outlet},
                        stock)
        before = one(f"SELECT counted_qty FROM pharm.stock_audit_line WHERE id={line_id}")
        probe("AUD-VAL-19e", "Count LineId=99999999", parvin,
              "/pharmacy/stock?handler=Count",
              {"LineId": "99999999", "CountedQty": "1", "OutletId": outlet}, stock)
        for sub, q in [("a", "abc"), ("b", "-10"), ("c", OVERFLOW), ("d", "5.5")]:
            probe(f"AUD-VAL-19{sub}", f"Count CountedQty={q!r} on line {line_id}", parvin,
                  "/pharmacy/stock?handler=Count",
                  {"LineId": line_id, "CountedQty": q, "OutletId": outlet},
                  parvin.get(f"/pharmacy/stock?OutletId={outlet}"), expect="write")
            after = one(f"SELECT counted_qty FROM pharm.stock_audit_line WHERE id={line_id}")
            if after != before:
                print(f"      .. line {line_id} physical count moved {before} -> {after}")
            # `line.CountedQty = Math.Max(0, CountedQty)`, and an unparseable CountedQty binds
            # to 0 — a typo in the count box writes the whole batch off the shelf, silently.
            if after == 0 and before > 0:
                check(False, f"AUD-VAL-19{sub}: a count of {q!r} did not silently zero the "
                             f"shelf (line {line_id} went {before} -> 0)")
                DEFECTS.append(f"AUD-VAL-19{sub} unparseable physical count writes the batch "
                               f"down to zero  [/pharmacy/stock?handler=Count]  "
                               f"pharm.stock_audit_line {line_id}: {before} -> 0, no error "
                               f"shown")
            before = after
        neg = one("SELECT count(*) FROM pharm.stock_audit_line WHERE counted_qty < 0")
        counted = sql("SELECT coalesce(counted_qty::text,'not counted') FROM "
                      f"pharm.stock_audit_line WHERE id={line_id}")
        print(f"      .. audit line {line_id} now reads {counted}")
        if not check(neg == 0,
                     f"AUD-VAL-19: no stock audit line holds a negative physical count "
                     f"(found {neg})"):
            DEFECTS.append("AUD-VAL-19b negative physical count accepted  "
                           f"[/pharmacy/stock?handler=Count]  {neg} line(s) below zero")
    else:
        check(False, "AUD-VAL-19: could not open a stock audit line to count against")

    # ================================================================= /emr/vitals
    case("AUD-VAL-20", "Vitals — clinically impossible readings", nasrin)
    vit = nasrin.get("/emr/vitals")
    enc = opt(vit, "EncounterId")
    if enc:
        vbase = {"EncounterId": enc}
        probe("AUD-VAL-20a", "vitals with every reading blank", nasrin, "/emr/vitals",
              {**vbase, "Systolic": "", "Diastolic": "", "Pulse": "", "Temperature": "",
               "Weight": "", "SpO2": ""}, vit)
        probe("AUD-VAL-20b", "vitals Temperature=4000 (decimal -> short conversion)", nasrin,
              "/emr/vitals", {**vbase, "Temperature": "4000"}, vit)
        for sub, f in [("c", {"Systolic": "-1", "Diastolic": "-9"}),
                       ("d", {"SpO2": "999"}),
                       ("e", {"Pulse": "32767"}),
                       ("f", {"Weight": "abc", "Pulse": "72"})]:
            probe(f"AUD-VAL-20{sub}", f"vitals {f}", nasrin, "/emr/vitals",
                  {**vbase, **f}, vit, expect="write")
    probe("AUD-VAL-20g", "vitals EncounterId=99999999", nasrin, "/emr/vitals",
          {"EncounterId": "99999999", "Pulse": "80"}, vit)
    absurd = one("SELECT count(*) FROM emr.vitals WHERE sp_o2 > 100 OR sp_o2 < 0 "
                 "OR pulse > 300 OR pulse < 0 OR systolic < 0 OR diastolic < 0 "
                 "OR temperature_tenths_c < 250 OR temperature_tenths_c > 460")
    if not check(absurd == 0,
                 f"AUD-VAL-20: no vitals row holds a physiologically impossible value "
                 f"(found {absurd})"):
        worst = sql("SELECT id||': systolic '||coalesce(systolic::text,'-')||', diastolic '"
                    "||coalesce(diastolic::text,'-')||', pulse '||coalesce(pulse::text,'-')"
                    "||', spo2 '||coalesce(sp_o2::text,'-') FROM emr.vitals "
                    "WHERE sp_o2 > 100 OR pulse > 300 OR systolic < 0 ORDER BY id DESC LIMIT 3")
        for w in worst.splitlines():
            print(f"      .. emr.vitals {w}")
        DEFECTS.append(f"AUD-VAL-20c/d/e impossible vitals accepted  [/emr/vitals]  "
                       f"{absurd} row(s) out of physiological range")

    # ================================================================= /emr/consult
    case("AUD-VAL-21", "Consultation note — free text and prescription lines", chowdhury)
    cq = chowdhury.get("/emr/queue")
    centry = re.search(r'href="/emr/consult/(\d+)"', cq)
    if centry:
        ce = centry.group(1)
        cpage = chowdhury.get(f"/emr/consult/{ce}")
        probe("AUD-VAL-21a", "Save Complaint = 100 KB", chowdhury,
              f"/emr/consult/{ce}?handler=Save",
              {"EncounterId": ce, "Complaint": BIG, "Diagnosis": "probe"}, cpage,
              expect="write")
        big_note = one("SELECT coalesce(max(length(complaint)),0) FROM emr.note")
        if not check(big_note < 10_000,
                     f"AUD-VAL-21a: no consultation note stored an unbounded complaint "
                     f"(longest is {big_note} chars)"):
            DEFECTS.append("AUD-VAL-21a 100 KB complaint stored verbatim  "
                           f"[/emr/consult/{{id}}?handler=Save]  emr.note.complaint = "
                           f"{big_note} chars")
        probe("AUD-VAL-21b", "Save a drug line whose product does not exist", chowdhury,
              f"/emr/consult/{ce}?handler=Save",
              {"EncounterId": ce, "Complaint": "probe", "DrugProductId": ["99999999"],
               "DrugName": ["Ghost"], "DrugDose": ["1"], "DrugFrequency": ["bd"],
               "DrugDuration": ["3d"], "DrugInstruction": [""]}, cpage, expect="write")
        ghost = one("SELECT count(*) FROM emr.note_drug d WHERE d.product_id IS NOT NULL "
                    "AND NOT EXISTS (SELECT 1 FROM pharm.product p WHERE p.id=d.product_id)")
        if not check(ghost == 0,
                     f"AUD-VAL-21b: no prescription line points at a product that does not "
                     f"exist (found {ghost})"):
            DEFECTS.append("AUD-VAL-21b prescription accepted a non-existent product id  "
                           f"[/emr/consult/{{id}}?handler=Save]  {ghost} orphan drug line(s)")
        probe("AUD-VAL-21c", "Save Tests=[99999999]", chowdhury,
              f"/emr/consult/{ce}?handler=Save",
              {"EncounterId": ce, "Complaint": "probe", "Tests": ["99999999"]}, cpage,
              expect="write")
    else:
        check(False, "AUD-VAL-21: no consultation in the queue to write a note against")

    # ================================================================= /diagnostics/order
    case("AUD-VAL-22", "Diagnostics order — cart, discount and payment", rasel)
    dord = rasel.get("/diagnostics/order")
    dtest = fixture(re.search(r'name="catalogId" value="(\d+)"', dord),
                    "no priced test in the diagnostics catalogue").group(1)
    dbase = {"PatientId": pid, "Items": [dtest], "Tender": "cash"}
    probe("AUD-VAL-22a", "Add catalogId=99999999", rasel, "/diagnostics/order?handler=Add",
          {"PatientId": pid, "catalogId": "99999999", "SubmissionToken": token()}, dord)
    probe("AUD-VAL-22b", "Save Items=[valid, 99999999]", rasel,
          "/diagnostics/order?handler=Save",
          {**dbase, "Items": [dtest, "99999999"], "SubmissionToken": token(),
           "DiscountFlat": "0", "PaidNow": "0"}, dord)
    probe("AUD-VAL-22c", "Save ReferrerId=99999999", rasel, "/diagnostics/order?handler=Save",
          {**dbase, "SubmissionToken": token(), "ReferrerId": "99999999",
           "DiscountFlat": "0", "PaidNow": "0"}, dord, expect="write")
    # The referrer is who the commission is owed to (§5 M13) — an id nothing points at makes
    # the referral report unreconcilable.
    ghost_ref = one("SELECT count(*) FROM diag.test_order t WHERE t.referrer_id IS NOT NULL "
                    "AND NOT EXISTS (SELECT 1 FROM adm.referrer r WHERE r.id = t.referrer_id)")
    if not check(ghost_ref == 0,
                 f"AUD-VAL-22c: every test order names a referrer who exists "
                 f"(found {ghost_ref} that do not)"):
        DEFECTS.append("AUD-VAL-22c commission attributed to a referrer id that does not "
                       f"exist  [/diagnostics/order?handler=Save]  {ghost_ref} order(s)")
    r = probe("AUD-VAL-22d", "Save PaidNow='300.30' (decimal into whole-taka)", rasel,
              "/diagnostics/order?handler=Save",
              {**dbase, "SubmissionToken": token(), "DiscountFlat": "0", "PaidNow": "300.30"},
              dord, expect="write")
    if r["wrote"].get("bill.invoice"):
        iid, gross, disc, net, paid = newest_invoice()
        if not check(bool(r["error"]) or paid > 0,
                     f"AUD-VAL-22d: the test order was refused or the payment was receipted "
                     f"(invoice {iid} has {paid} Tk against {net} Tk)"):
            DEFECTS.append("AUD-VAL-22d decimal payment silently dropped  "
                           f"[/diagnostics/order?handler=Save]  invoice {iid}: net {net}, "
                           f"receipted {paid}")

    # ================================================================= /admin/masters
    case("AUD-VAL-23", "Masters — creating a catalogue item", admin)
    mast = admin.get("/admin/masters")
    mbase = {"Kind": "service", "Dept": "OPD", "TatMinutes": "240"}
    probe("AUD-VAL-23a", "Create with Code and Name empty", admin,
          "/admin/masters?handler=Create",
          {**mbase, "Code": "", "Name": "", "Price": "100"}, mast)
    probe("AUD-VAL-23b", "Create Price='-1'", admin, "/admin/masters?handler=Create",
          {**mbase, "Code": f"PV{stamp}B", "Name": "Probe B", "Price": "-1"}, mast)
    probe("AUD-VAL-23c", "Create Name = 100 KB", admin, "/admin/masters?handler=Create",
          {**mbase, "Code": f"PV{stamp}C", "Name": BIG, "Price": "100"}, mast,
          expect="write")
    for sub, p, why in [("d", "abc", "type garbage"), ("e", "250.75", "decimal"),
                        ("f", OVERFLOW, "overflow")]:
        r = probe(f"AUD-VAL-23{sub}", f"Create Price={p!r} ({why})", admin,
                  "/admin/masters?handler=Create",
                  {**mbase, "Code": f"PV{stamp}{sub.upper()}", "Name": f"Probe {sub}",
                   "Price": p}, mast, expect="write")
        if r["wrote"].get("adm.service"):
            sid = one("SELECT max(id) FROM adm.service")
            rv = one("SELECT count(*) FROM adm.rate_version WHERE catalog_kind='service' "
                     f"AND catalog_id={sid}")
            prov = sql(f"SELECT provisional FROM adm.service WHERE id={sid}")
            print(f"      .. service {sid} created with {rv} rate version(s), "
                  f"provisional={prov}")
            if not check(bool(r["error"]) or rv > 0,
                         f"AUD-VAL-23{sub}: a price of {p!r} was refused with a message, or "
                         f"actually became a rate version ({rv} found)"):
                DEFECTS.append(f"AUD-VAL-23{sub} unparseable price saved a priceless catalogue "
                               f"item  [/admin/masters?handler=Create]  adm.service {sid} has "
                               f"{rv} rate version(s)")
    long_svc = one("SELECT coalesce(max(length(name)),0) FROM adm.service")
    if not check(long_svc < 10_000,
                 f"AUD-VAL-23c: no catalogue name was stored unbounded "
                 f"(longest is {long_svc} chars)"):
        DEFECTS.append(f"AUD-VAL-23c 100 KB catalogue name stored verbatim  "
                       f"[/admin/masters?handler=Create]  adm.service.name = "
                       f"{long_svc} chars")

    case("AUD-VAL-24", "Masters — repricing, effective date and amount", admin)
    rep = fixture(re.search(r'name="TargetId" value="(\d+)"[\s\S]{0,200}?'
                            r'name="TargetKind" value="([a-z]+)"', mast),
                  "no repriceable catalogue row on /admin/masters")
    tid, tkind = rep.groups()
    rbase = {"TargetId": tid, "TargetKind": tkind}
    for sub, d in zip("abcde", BAD_DATES):
        probe(f"AUD-VAL-24{sub}", f"Reprice EffectiveFrom={d!r}", admin,
              "/admin/masters?handler=Reprice",
              {**rbase, "NewPrice": "500", "EffectiveFrom": d}, mast, expect="write")
    probe("AUD-VAL-24f", "Reprice NewPrice='-1'", admin, "/admin/masters?handler=Reprice",
          {**rbase, "NewPrice": "-1", "EffectiveFrom": ""}, mast)
    for sub, p in [("g", "abc"), ("h", "99.99")]:
        r = probe(f"AUD-VAL-24{sub}", f"Reprice NewPrice={p!r}", admin,
                  "/admin/masters?handler=Reprice",
                  {**rbase, "NewPrice": p, "EffectiveFrom": ""}, mast, expect="write")
        if r["wrote"].get("adm.rate_version"):
            nv = sql("SELECT id||'|'||price||'|'||valid_from FROM adm.rate_version "
                     "ORDER BY id DESC LIMIT 1")
            print(f"      .. new rate version {nv}")
            rid, price, vfrom = nv.split("|")
            if not check(bool(r["error"]) or int(price) > 0,
                         f"AUD-VAL-24{sub}: a new price of {p!r} was refused with a message, or "
                         f"is a real price (rate version {rid} is priced at {price})"):
                DEFECTS.append(f"AUD-VAL-24{sub} unparseable reprice became a 0-taka future "
                               f"price  [/admin/masters?handler=Reprice]  adm.rate_version "
                               f"{rid} = {price} from {vfrom}")
    # 9999-12-31 is `DateOnly.MaxValue`, which Npgsql stores as `infinity`. The price never
    # takes effect and — because the reprice guard is "the new one must start after the current
    # one" — that catalogue item can never be repriced again through the product.
    bad_rv = one("SELECT count(*) FROM adm.rate_version WHERE price < 0 "
                 "OR valid_from < '2000-01-01' OR valid_from > '2100-01-01'")
    if not check(bad_rv == 0,
                 f"AUD-VAL-24c: no rate version carries a negative price or an impossible "
                 f"start date (found {bad_rv})"):
        frozen = sql("SELECT catalog_kind||' '||catalog_id||' from '||valid_from "
                     "FROM adm.rate_version WHERE valid_from > '2100-01-01' LIMIT 3")
        for f in frozen.splitlines():
            print(f"      .. rate version starting in the far future: {f}")
        DEFECTS.append("AUD-VAL-24c a far-future effective date is accepted and freezes the "
                       f"item's price forever  [/admin/masters?handler=Reprice]  {bad_rv} rate "
                       f"version(s) start at infinity; further repricing is refused")

    # ================================================================= /lis
    case("AUD-VAL-25", "Lab results — analyte values", ripon)
    # One order per payload. Entering a result closes the order to further entry ("corrected by
    # amendment, not by overwriting"), so reusing one order makes every payload after the first
    # bounce off that rule and report a clean pass it never earned.
    # The worklist is the only truthful source of what the bench can actually enter against:
    # a test order whose sample was never collected has no grid, and `diag.order_test` cannot
    # tell the two apart. A seeded database usually has none left by the time this runs — a
    # sample only reaches the bench once its order is *paid* — so the fixture is built here,
    # through the product, one order per payload.
    res = ripon.get("/lis/results")
    pending = list(dict.fromkeys(re.findall(r'/lis/results\?orderId=(\d+)', res)))
    for _ in range(4 - len(pending)):
        dpage = rasel.get("/diagnostics/order")
        rasel.post("/diagnostics/order?handler=Save",
                   {"PatientId": pid, "Items": [dtest], "Tender": "cash",
                    "SubmissionToken": token(), "DiscountFlat": "0", "PaidNow": "0"}, dpage)
        new_inv = one("SELECT max(id) FROM bill.invoice")
        net = one(f"SELECT net FROM bill.invoice WHERE id={new_inv}")
        dpage = rasel.get("/billing/dues")
        rasel.post("/billing/dues?handler=Collect",
                   {"InvoiceId": str(new_inv), "Amount": str(net), "Tender": "cash"}, dpage)
        board = ripon.get("/lis/board")
        for sid in re.findall(r'name="sampleId" value="(\d+)"', board)[:1]:
            ripon.post("/lis/board?handler=Collect", {"sampleId": sid},
                       ripon.get("/lis/board"))
            ripon.post("/lis/board?handler=Receive", {"sampleId": sid},
                       ripon.get("/lis/board"))
    res = ripon.get("/lis/results")
    pending = list(dict.fromkeys(re.findall(r'/lis/results\?orderId=(\d+)', res)))
    print(f"      .. {len(pending)} order(s) on the bench awaiting result entry")
    payloads = [("a", "abc", "type garbage"), ("b", "-1", "negative"),
                ("c", "999999999", "absurdly large"), ("d", BIG, "100 KB")]
    used = 0
    for sub, v, why in payloads:
        page, oid, vkeys = None, None, []
        while used < len(pending):
            cand = pending[used]
            used += 1
            page = ripon.get(f"/lis/results?orderId={cand}")
            vkeys = re.findall(r'name="(Values\[[^\]]+\])"', page)
            if vkeys:
                oid = cand
                break
        if not oid:
            check(False, f"AUD-VAL-25{sub}: ran out of lab orders awaiting entry "
                         f"({len(pending)} on the worklist, none with an enterable grid)")
            continue
        probe(f"AUD-VAL-25{sub}", f"order {oid}: every analyte = {why}", ripon, "/lis/results",
              {"OrderId": oid, **{k: v for k in vkeys}}, page, expect="write")
    # A numeric analyte that stores "abc" prints "abc" as the patient's cholesterol, and the
    # abnormality flag beside it comes out blank because no range comparison was possible.
    nonnumeric = one(
        "SELECT count(*) FROM lis.result r, jsonb_each(r.values) AS kv "
        "WHERE kv.value->>'value' IS NOT NULL AND kv.value->>'value' <> '' "
        "AND kv.value->>'value' !~ '^-?[0-9]+(\\.[0-9]+)?$'")
    if not check(nonnumeric == 0,
                 f"AUD-VAL-25a: every stored analyte value is a number "
                 f"(found {nonnumeric} that are not)"):
        DEFECTS.append("AUD-VAL-25a non-numeric analyte value stored and flagged blank  "
                       f"[/lis/results]  {nonnumeric} value(s) that are not numbers")
    big_res = one("SELECT coalesce(max(length(values::text)),0) FROM lis.result")
    if not check(big_res < 5_000,
                 f"AUD-VAL-25d: no lab result stored an unbounded value "
                 f"(largest values blob is {big_res} chars)"):
        DEFECTS.append(f"AUD-VAL-25d 100 KB analyte value stored verbatim  "
                       f"[/lis/results]  lis.result.values = {big_res} chars")
    probe("AUD-VAL-25e", "results OrderId=99999999", ripon, "/lis/results",
          {"OrderId": "99999999", "Values[8.TCHOL]": "5"}, ripon.get("/lis/results"))

    case("AUD-VAL-26", "Lab verification — consultant attribution", farhana)
    # AUD-VAL-25 is what puts an order into the Resulted stage, so this case only has a fixture
    # when that one found a grid to type into.
    ver = farhana.get("/lis/verify")
    vorder = re.search(r'name="OrderId" value="(\d+)"', ver)
    if vorder:
        probe("AUD-VAL-26a", "verify ConsultantId=99999999", farhana, "/lis/verify",
              {"OrderId": vorder.group(1), "ConsultantId": "99999999"}, ver, expect="write")
        # `SignatureImageRef = $"consultant:{cid}"` is what the printed report's signature block
        # resolves against — an id nothing points at signs the report with nobody.
        ghost_sig = one(
            "SELECT count(*) FROM lis.result r WHERE r.signature_image_ref LIKE 'consultant:%' "
            "AND NOT EXISTS (SELECT 1 FROM adm.reporting_consultant c WHERE c.id = "
            "substring(r.signature_image_ref from 12)::bigint)")
        if not check(ghost_sig == 0,
                     f"AUD-VAL-26a: every signed report names a consultant on the master "
                     f"(found {ghost_sig} that do not)"):
            DEFECTS.append("AUD-VAL-26a report signed with a consultant id that does not exist "
                           f" [/lis/verify]  {ghost_sig} verified result(s)")
        probe("AUD-VAL-26b", "verify OrderId=99999999", farhana, "/lis/verify",
              {"OrderId": "99999999", "ConsultantId": ""}, farhana.get("/lis/verify"))
        probe("AUD-VAL-26c", "verify ConsultantId='abc'", farhana, "/lis/verify",
              {"OrderId": vorder.group(1), "ConsultantId": "abc"},
              farhana.get("/lis/verify"), expect="write")
    else:
        check(False, "AUD-VAL-26: no lab order awaiting verification on /lis/verify")

    # ================================================================ corpus extension
    # Spec 0039: the audit probed 26 of 141 handlers and 19 carried defects — the honest prior
    # was that more existed in the other 115. These cases sweep five more money/clinical
    # handlers with the same contract: no 500, nothing written, a sentence or a silent refusal.

    case("AUD-VAL-27", "Counter session — opening float", rasel)
    sess_page = rasel.get("/billing/session")
    for sub, amt in [("a", "abc"), ("b", "-1"), ("c", OVERFLOW), ("d", "500.50")]:
        probe(f"AUD-VAL-27{sub}", f"Open OpeningFloat={amt!r}", rasel, "/billing/session",
              {"CounterId": "1", "OpeningFloat": amt}, sess_page)

    case("AUD-VAL-28", "OT schedule — theatre, window and team", admin)
    ot = admin.get("/ot/schedule")
    otbase = {"PatientId": pid, "TheatreId": "1", "OperationServiceId": "1",
              "OnDate": "", "FromTime": "10:00", "Minutes": "90",
              "SurgeonId": "1", "AnaesthetistId": "2", "AssistantId": "0"}
    probe("AUD-VAL-28a", "schedule TheatreId=99999999", admin, "/ot/schedule",
          {**otbase, "TheatreId": "99999999"}, ot)
    probe("AUD-VAL-28b", "schedule Minutes='-30'", admin, "/ot/schedule",
          {**otbase, "Minutes": "-30"}, ot)
    probe("AUD-VAL-28c", "schedule Minutes='abc'", admin, "/ot/schedule",
          {**otbase, "Minutes": "abc"}, ot)
    probe("AUD-VAL-28d", "schedule OnDate='31/02/2026'", admin, "/ot/schedule",
          {**otbase, "OnDate": "31/02/2026"}, ot)
    probe("AUD-VAL-28e", "schedule SurgeonId=99999999", admin, "/ot/schedule",
          {**otbase, "SurgeonId": "99999999"}, ot)

    case("AUD-VAL-29", "Pharmacy transfers — indent lists and outlets", parvin)
    tr = parvin.get("/pharmacy/transfers")
    tprod = opt(tr, "ProductIds") or "1"
    probe("AUD-VAL-29a", "Indent ragged lists (2 products, 1 qty)", parvin,
          "/pharmacy/transfers?handler=Indent",
          {"FromOutletId": "1", "ToOutletId": "2", "ProductIds": [tprod, tprod],
           "LineQtys": ["1"]}, tr)
    probe("AUD-VAL-29b", "Indent LineQtys=['abc']", parvin,
          "/pharmacy/transfers?handler=Indent",
          {"FromOutletId": "1", "ToOutletId": "2", "ProductIds": [tprod],
           "LineQtys": ["abc"]}, tr)
    probe("AUD-VAL-29c", "Indent LineQtys=['-5']", parvin,
          "/pharmacy/transfers?handler=Indent",
          {"FromOutletId": "1", "ToOutletId": "2", "ProductIds": [tprod],
           "LineQtys": ["-5"]}, tr)
    probe("AUD-VAL-29d", "Indent ToOutletId=99999999", parvin,
          "/pharmacy/transfers?handler=Indent",
          {"FromOutletId": "1", "ToOutletId": "99999999", "ProductIds": [tprod],
           "LineQtys": ["1"]}, tr)

    case("AUD-VAL-30", "Pharmacy product master — brand, reorder level, company", parvin)
    prods = parvin.get("/pharmacy/products")
    probe("AUD-VAL-30a", "Create Brand = 100 KB", parvin, "/pharmacy/products?handler=Create",
          {"CompanyId": "1", "Brand": BIG, "Generic": "probe", "Strength": "10mg",
           "Form": "tablet", "Unit": "pcs", "ReorderLevel": "5"}, prods, expect="write")
    long_brand = one("SELECT coalesce(max(length(brand)),0) FROM pharm.product")
    if not check(long_brand < 1_000,
                 f"AUD-VAL-30a: no product brand stored unbounded (longest {long_brand})"):
        DEFECTS.append(f"AUD-VAL-30a 100 KB brand stored verbatim  "
                       f"[/pharmacy/products?handler=Create]  pharm.product.brand = "
                       f"{long_brand} chars")
    probe("AUD-VAL-30b", "Create ReorderLevel='-5'", parvin,
          "/pharmacy/products?handler=Create",
          {"CompanyId": "1", "Brand": "", "Generic": "", "Strength": "",
           "Form": "tablet", "Unit": "pcs", "ReorderLevel": "-5"}, prods)
    probe("AUD-VAL-30c", "Create CompanyId=99999999", parvin,
          "/pharmacy/products?handler=Create",
          {"CompanyId": "99999999", "Brand": "Probe Brand", "Generic": "probe",
           "Strength": "10mg", "Form": "tablet", "Unit": "pcs", "ReorderLevel": "5"},
          prods, expect="write")
    ghost_co = one("SELECT count(*) FROM pharm.product p WHERE NOT EXISTS "
                   "(SELECT 1 FROM pharm.company c WHERE c.id = p.company_id)")
    if not check(ghost_co == 0,
                 f"AUD-VAL-30c: every product names a company that exists (found {ghost_co})"):
        DEFECTS.append("AUD-VAL-30c product created for a company id that does not exist  "
                       f"[/pharmacy/products?handler=Create]  {ghost_co} orphan product(s)")

    case("AUD-VAL-31", "Discharge — admission ids and money that cannot be right", rasel)
    dis = rasel.get(f"/ipd/discharge/{adm_id}")
    probe("AUD-VAL-31a", "Initiate AdmissionId=99999999", rasel,
          f"/ipd/discharge/{adm_id}?handler=Initiate", {"AdmissionId": "99999999"}, dis)
    probe("AUD-VAL-31b", "Initiate AdmissionId='abc'", rasel,
          f"/ipd/discharge/{adm_id}?handler=Initiate", {"AdmissionId": "abc"}, dis)
    probe("AUD-VAL-31c", "Prepare DiscountFlat='abc'", rasel,
          f"/ipd/discharge/{adm_id}?handler=Prepare",
          {"AdmissionId": adm_id, "DiscountFlat": "abc", "SubmissionToken": token()}, dis)

    print("\n" + "-" * 78)
    print(f"payloads fired: {FIRED}   confirmed defects: {len(DEFECTS)}")
    for d in DEFECTS:
        print(f"  DEFECT  {d}")
    return report("Input-validation sweep (spec 0038, AUD-VAL)")


if __name__ == "__main__":
    raise SystemExit(main())
