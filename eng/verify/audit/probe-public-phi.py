#!/usr/bin/env python3
"""Unauthenticated surfaces and what they give away (spec 0038, find-and-report).

Four routes in this product answer without a login — the lobby queue board, the "is my report
ready?" lookup, `/health`, and the login page itself — and one more, `/denied`, is anonymous-
reachable by accident rather than by design. §8 N5 and the P15 default say a public screen may
carry a serial and a masked name, never a full identity, an amount or a diagnosis.

This probe asks the questions a stranger with a browser would ask:

  * does the queue board print a patient's real name, or the masked form?
  * is the report lookup **enumerable** — can a stranger walk order numbers and read back other
    people's names and test lists without knowing anything about them?
  * does the patient type-ahead answer anonymously, and when a low-privilege operator calls it,
    does the JSON carry more about the patient than the picker actually shows?
  * does an anonymous hit on `/denied` bounce between login and denial?
  * does `/health` volunteer a version or a connection string?

The queue check needs a patient actually in a chamber, and the seeded day usually has none. Rather
than pass vacuously the probe **drives the product** to put one there — as the receptionist,
through the same Issue and Advance handlers the front desk uses — reads the public board, and then
advances the appointment to Done, which is where that serial was going anyway. Nothing is deleted
(rule 4) and no row is written by hand. A serial is consumed at issue and never returned (spec
0032 M3-D1), so a run that has to issue one costs the day one appointment row; that is the only
residue either probe leaves.

Run (ERP + HRM up, seeded):
    python3 eng/verify/audit/probe-public-phi.py
"""
import datetime as dt
import json
import os
import re
import subprocess
import sys
import urllib.error
import urllib.request

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
import _harness as H  # noqa: E402
from _harness import case, check, report  # noqa: E402

ERP = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
HRM = os.environ.get("HRM_URL", "http://localhost:5299").rstrip("/")
CONTAINER = os.environ.get("HMS_DEV_DB", "hms-dev-db")
ERP_DB = os.environ.get("HMS_DB", "hms")


def sql(q: str, db: str = ERP_DB) -> str:
    out = subprocess.run(
        ["docker", "exec", CONTAINER, "psql", "-U", "postgres", "-d", db, "-tAc", q],
        capture_output=True, text=True, check=True)
    return out.stdout.strip()


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


# No cookie jar at all: an anonymous caller is not a logged-out session, it is a stranger who has
# never touched this host. Reusing a Session with its cookies cleared would still carry the
# antiforgery cookie and would not be the thing under test.
ANON = urllib.request.build_opener()
ANON_RAW = urllib.request.build_opener(_NoRedirect)


def anon(url: str) -> tuple[int, str, dict]:
    """GET as a stranger, following redirects. Returns (status, body, headers)."""
    try:
        with ANON.open(url) as r:
            return r.getcode(), r.read().decode(errors="replace"), dict(r.headers)
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode(errors="replace"), dict(e.headers)


def anon_hop(url: str) -> tuple[int, str]:
    """One hop, no redirect following — (status, Location)."""
    try:
        with ANON_RAW.open(url) as r:
            return r.getcode(), r.headers.get("Location", "")
    except urllib.error.HTTPError as e:
        return e.code, e.headers.get("Location", "")


def masked(full_name: str) -> str:
    """Ui.MaskName — "Rahim Uddin" -> "Rahim U."."""
    parts = full_name.split()
    if not parts:
        return "—"
    return parts[0] if len(parts) == 1 else f"{parts[0]} {parts[-1][0].upper()}."


def main() -> int:
    H.guard("t1")
    H.BASE = ERP
    print(f"ERP {ERP} (db {ERP_DB})   HRM {HRM}")

    # ------------------------------------------------------------------ AUD-PHI-01
    case("AUD-PHI-01", "The lobby queue board shows a serial and a masked name, never an identity",
         "jashim")
    # The board's "today" is Asia/Dhaka, which is not the container's `current_date` for six hours
    # of every UTC day. Asking psql for `current_date` is how this probe first "proved" the board
    # masks a name it was in fact never rendering.
    today = (dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=6)).strftime("%Y-%m-%d")
    jashim = H.Session("jashim")
    # The board is built from the *schedule* table and shows a doctor's chamber; an appointment
    # whose doctor has no schedule row can never appear on it, however live it is. One such row
    # (doctor 99999999, left by another thread) is enough to make this case pass by accident.
    live = (f"SELECT a.id||'|'||a.patient_id||'|'||a.state FROM appt.appointment a "
            f"WHERE a.on_date = DATE '{today}' AND a.state IN ('booked','arrived','in_chamber') "
            "AND EXISTS (SELECT 1 FROM appt.doctor_schedule d WHERE d.doctor_id = a.doctor_id) "
            "ORDER BY a.id LIMIT 1")
    appt = sql(live)
    if not appt:
        # No live serial on today's board. Issue one through the front desk's own screen rather
        # than skip the check — a queue monitor with nothing on it proves nothing about masking.
        patient = sql("SELECT id FROM reg.patient WHERE full_name LIKE '% %' "
                      "AND active ORDER BY id LIMIT 1")
        doctor = sql("SELECT min(doctor_id) FROM appt.doctor_schedule")
        page = jashim.get("/appointments")
        jashim.post("/appointments?handler=Issue",
                    {"PatientId": patient, "DoctorId": doctor}, page)
        print(f"      .. no serial on today's ({today}) board — issued one for patient {patient}")
        appt = sql(live)
    restore = None
    if appt:
        appt_id, patient_id, state = appt.split("|")
        full = sql(f"SELECT full_name FROM reg.patient WHERE id = {patient_id}")
        print(f"      .. appointment {appt_id} ({state}) is patient {patient_id} — "
              f"'{full}', masked '{masked(full)}'")

        # Walk it to in_chamber through the product, exactly as the front desk does.
        for frm, to in (("booked", "arrived"), ("arrived", "in_chamber")):
            if sql(f"SELECT state FROM appt.appointment WHERE id={appt_id}") == frm:
                page = jashim.get("/appointments")
                jashim.post("/appointments?handler=Advance",
                            {"id": appt_id, "from": frm, "to": to}, page)
        now = sql(f"SELECT state FROM appt.appointment WHERE id={appt_id}")
        check(now == "in_chamber",
              f"appointment {appt_id} is in the chamber, so the board has a name to print "
              f"(state={now})")
        restore = (jashim, appt_id)

        status, body, _ = anon(ERP + "/public/queue")
        check(status == 200, f"GET /public/queue anonymously returns {status}")
        check(full not in body,
              f"the board does NOT print the full identity '{full}'")
        check(masked(full) in body,
              f"the board prints the masked form '{masked(full)}' instead")
        # Nothing else on the board may identify or price the patient.
        for field, label in (("phone", "phone number"), ("uhid", "UHID"),
                             ("nid", "national id"), ("address", "address")):
            value = sql(f"SELECT coalesce({field},'') FROM reg.patient WHERE id = {patient_id}")
            if value:
                check(value not in body, f"the board does not print the patient's {label}")
        check("৳" not in body, "the board prints no money")
    else:
        check(False, "no appointment today to put in a chamber — queue board untested "
                     "(reseed: the demo seed includes today)")

    # ------------------------------------------------------------------ AUD-PHI-02
    case("AUD-PHI-02", "Report status: can a stranger walk order numbers and read other patients?",
         "anonymous")
    real = sql("SELECT count(*) FROM diag.test_order WHERE state <> 'cancelled'")
    top = int(sql("SELECT coalesce(max(id),0) FROM diag.test_order") or 0)
    span = list(range(1, top + 25))              # every real order and a stretch past the end
    hits, disclosed = [], []
    for oid in span:
        _, body, _ = anon(f"{ERP}/public/report-status?orderNo=LB-{oid:05d}")
        card = re.search(r'class="card-head">([^<]+)</div>', body)
        if card and "No report found" not in body:
            tests = re.findall(r'<span>([^<]+)</span>\s*<span class="spacer">', body)
            hits.append(oid)
            disclosed.append((oid, card.group(1).strip(), tests))
    print(f"      .. {len(hits)} of {len(span)} probed order numbers answered with a record")
    for oid, head, tests in disclosed[:5]:
        print(f"      .. LB-{oid:05d} -> {head} :: {', '.join(tests) or '(no test names)'}")

    # The disclosure itself is by design (spec 0019 R3): the receipt's order number is treated as
    # the shared secret that entitles the bearer to a masked name and a per-test progress line.
    # The finding, if there is one, is that the "secret" is `1, 2, 3, …` — the raw primary key,
    # zero-padded. `orderNo` is reduced to its digits and parsed straight into an id, so LB-00007
    # is order 7 and there is nothing else to know.
    check(len(hits) == 0,
          f"a stranger who knows no order number retrieves nothing — {len(hits)} of "
          f"{int(real or 0)} live orders were read back by walking LB-00001..LB-{span[-1]:05d}, "
          "with a masked name and the tests ordered for each")

    # The one control the page does have, and it works: a miss and a malformed number are
    # indistinguishable, so the *end* of the series cannot be found by probing.
    _, miss, _ = anon(f"{ERP}/public/report-status?orderNo=LB-{top + 500:05d}")
    _, junk, _ = anon(f"{ERP}/public/report-status?orderNo=NOT-A-NUMBER")
    check("No report found" in miss and "No report found" in junk,
          "an unknown and a malformed order number give the same answer (no id oracle)")

    # Masking is asserted against the real name for each order, not against a shape: the surname
    # initial of "Lifecycle 38682" is a digit, and a regex for [A-Z] would call that a leak.
    wrong = []
    for oid, head, _ in disclosed:
        shown = head.split("—")[-1].strip()
        full = sql(f"SELECT p.full_name FROM reg.patient p "
                   f"JOIN diag.test_order o ON o.patient_id = p.id WHERE o.id = {oid}")
        if shown != masked(full):
            wrong.append((oid, full, shown))
    check(not wrong,
          f"every name the lookup returns is the masked form of the real one "
          f"({len(disclosed)} checked{'; WRONG ' + str(wrong) if wrong else ''})")

    # ------------------------------------------------------------------ AUD-PHI-03
    case("AUD-PHI-03", "The patient type-ahead: anonymous access, and what it hands a low-privilege "
                       "operator", "moinul")
    code, location = anon_hop(ERP + "/api/typeahead/patients?q=ra")
    check(code in (401, 403) or (code in (301, 302) and "/login" in location),
          f"GET /api/typeahead/patients anonymously is refused ({code} -> {location or '-'})")

    # moinul is the narrowest holder of registration.read on the ERP cast: radiology only.
    moinul = H.Session("moinul")
    raw = moinul.get("/api/typeahead/patients?q=ra")
    rows = json.loads(raw)
    keys = sorted({k for row in rows for k in row})
    print(f"      .. {len(rows)} row(s), JSON keys {keys}")
    check(rows, "the endpoint answers a low-privilege authenticated operator (fixture present)")
    check(keys in ([], ["label", "value"]),
          f"the picker's JSON carries only the id and the label it displays (keys={keys})")

    # The label is what the operator sees on screen, so anything *inside* it is disclosed by the
    # UI too. Anything the JSON carries beyond the label would be an invisible leak.
    labels = " || ".join(r.get("label", "") for r in rows)
    for field, why in (("dob", "date of birth"), ("nid", "national id"),
                       ("address", "address"), ("area", "area"), ("guardian", "guardian")):
        vals = sql(f"SELECT string_agg(DISTINCT {field}::text, '|') FROM reg.patient "
                   f"WHERE {field} IS NOT NULL AND {field}::text <> ''")
        bad = [v for v in (vals.split("|") if vals else []) if v and v in raw]
        check(not bad, f"the type-ahead response carries no {why} ({len(bad)} match(es))")
    phones = sql("SELECT string_agg(DISTINCT phone, '|') FROM reg.patient WHERE phone <> ''")
    shown = [p for p in (phones.split("|") if phones else []) if p and p in labels]
    print(f"      .. {len(shown)} row(s) carry the patient's phone number inside the visible "
          f"label (by design — §7 U5 disambiguates two same-name patients by phone)")
    check(all(p in labels for p in shown),
          "every phone number in the response is inside the label the picker renders — "
          "nothing is carried that the screen does not already show")

    # ------------------------------------------------------------------ AUD-PHI-04
    case("AUD-PHI-04", "/denied has no [AllowAnonymous] — does a stranger loop between it and login?",
         "anonymous")
    seen, url, loop = [], ERP + "/denied", False
    for _ in range(8):
        status, location = anon_hop(url)
        seen.append(f"{url.replace(ERP, '')} -> {status}")
        if status not in (301, 302, 307) or not location:
            break
        nxt = location if location.startswith("http") else ERP + location
        if nxt in [s.split(" -> ")[0] for s in seen] or len(seen) > 4:
            loop = True
            break
        url = nxt
    print("      .. " + "  |  ".join(seen))
    check(not loop, f"an anonymous GET /denied settles in {len(seen)} hop(s), it does not loop")
    check(seen and seen[-1].endswith("200"),
          f"the chain ends on a page that renders ({seen[-1] if seen else 'no hops'})")
    # Where it ends matters as much as that it ends. `/login` carries no hidden ReturnUrl field,
    # so the value survives the POST only in the query string — which it does, because the form
    # posts back to the same URL. Rather than infer that, sign in and look at where we land.
    landed = H.Session("jashim")
    where, body = landed.post("/login?ReturnUrl=%2Fdenied",
                              {"Username": "jashim", "Password": H.DEV_PASSWORD},
                              landed.get("/login?ReturnUrl=%2Fdenied"))
    # Assert on the URL we ended on and on the denial screen's own wording. An early version of
    # this check looked for the word "denied" in the body — which the page never says — and
    # cheerfully passed while sitting on the access-denied screen.
    on_denied = where.endswith("/denied") or "This screen is not part of your role" in body
    check(not on_denied,
          "after signing in from the /denied bounce the operator lands on a working screen, "
          f"not on the access-denied page (landed on {where})")

    # ------------------------------------------------------------------ AUD-PHI-05
    case("AUD-PHI-05", "/health on both hosts answers a stranger without describing the machine",
         "anonymous")
    secret = ["Password", "password", "Host=", "Username=", "Database=", "postgres",
              "Npgsql", "Microsoft.", "5455", "Version", "version", "Stack", "at Hms."]
    for name, base in (("ERP", ERP), ("HRM", HRM)):
        status, body, headers = anon(base + "/health")
        check(status == 200, f"{name} GET /health anonymously -> {status}")
        check(body.strip() == '{"status":"ok"}',
              f"{name} /health answers exactly the liveness fact and nothing else: {body.strip()}")
        found = [t for t in secret if t in body]
        check(not found, f"{name} /health body names no connection or version detail ({found})")
        loud = {k: v for k, v in headers.items()
                if k.lower() in ("server", "x-powered-by", "x-aspnet-version",
                                 "x-aspnetmvc-version")
                and re.search(r"\d", v)}
        check(not loud, f"{name} /health leaks no version header ({loud or 'none'})")

    # -------------------------------------------------------- return the fixture
    if restore:
        jashim, appt_id = restore
        page = jashim.get("/appointments")
        jashim.post("/appointments?handler=Advance",
                    {"id": appt_id, "from": "in_chamber", "to": "done"}, page)
        print(f"\n  teardown — appointment {appt_id} advanced to "
              f"{sql(f'SELECT state FROM appt.appointment WHERE id={appt_id}')}")

    return report("Public / unauthenticated surface probe (spec 0038)")


if __name__ == "__main__":
    raise SystemExit(main())
