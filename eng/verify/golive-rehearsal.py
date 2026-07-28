#!/usr/bin/env python3
"""Spec 0023: rehearse RUNBOOK §9 — the go-live switch — end to end.

§9 has been written and correct on paper since spec 0015 and has never been executed once. A
procedure that has never run is a plan, not a safeguard. This script runs every step against a
scratch instance and proves each one, so the only thing standing between the product and a
clean go-live is the PM's written instruction (P16).

It never touches production: it drives whatever instance is at BASE (default the local
verification app on :5199) and expects that instance to be disposable. The caller boots the app;
this script performs the rotation and the proofs.

usage: python3 eng/verify/golive-rehearsal.py           (from the repo root)
"""
import http.cookiejar
import os
import re
import sys
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
DEMO_PASSWORD = "Demo#1234"
NEW_PASSWORD = "GoLive#2026!kx"
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')

# The accounts a real hospital keeps (real staff take them over) versus the demo cast that goes.
KEEPERS = ["admin", "md"]
RETIRED = ["jashim", "rasel", "ripon", "farhana", "shahid", "parvin", "nasrin"]


class Session:
    def __init__(self):
        self.op = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))

    def get(self, path):
        with self.op.open(BASE + path) as r:
            return r.geturl(), r.read().decode()

    def post(self, path, fields, page_html=None):
        html = page_html if page_html is not None else self.get(path)[1]
        data = dict(fields)
        m = TOKEN_RE.search(html)
        if m:
            data["__RequestVerificationToken"] = m.group(1)
        req = urllib.request.Request(
            BASE + path, data=urllib.parse.urlencode(data, doseq=True).encode())
        with self.op.open(req) as r:
            return r.geturl(), r.read().decode()

    def login(self, user, password):
        """True when the credential is accepted — the redirect leaves /login behind."""
        _, html = self.get("/login")
        try:
            url, body = self.post("/login", {"Username": user, "Password": password}, html)
        except urllib.error.HTTPError:
            return False
        return "/login" not in url


fail = []


def check(cond, msg):
    print(f"  {'✓' if cond else '✗'} {msg}")
    if not cond:
        fail.append(msg)


def signs_in(user, password):
    return Session().login(user, password)


print("GO-LIVE REHEARSAL (RUNBOOK §9)")

# ---- step 1: seeding off ---------------------------------------------------------
# This instance is booted with Seed:DevUsers=false. The control that the flag — and not luck —
# is what stops seeding runs in the wrapper (golive-rehearsal.sh): the same build over an EMPTY
# database with the flag off must produce zero accounts. Here we only confirm the cast this
# database already had is still present, because rotation, not absence, is what must kill it.
print("\n1. seeding is off; the existing cast is untouched by the boot")
admin = Session()
check(admin.login("admin", DEMO_PASSWORD),
      "the demo admin still signs in before rotation (the cast survives, seeding just stops)")
if fail:
    print("\nFAILED — cannot rehearse without a signed-in admin")
    sys.exit(1)

_, users_html = admin.get("/admin/users")

# ---- step 2: rotate the keepers, deactivate the rest ------------------------------
print("\n2. rotate the credentials that stay, deactivate the ones that do not")
id_by_name = {}
for row in re.finditer(r'<td class="id-cell">([a-z]+)</td>.*?name="id" value="(\d+)"',
                       users_html, re.S):
    id_by_name.setdefault(row.group(1), row.group(2))
check(len(id_by_name) >= 5, f"the user list is readable ({len(id_by_name)} accounts found)")

for user in KEEPERS:
    if user not in id_by_name:
        continue
    admin.post("/admin/users?handler=ResetPassword",
               {"id": id_by_name[user], "newPassword": NEW_PASSWORD},
               admin.get("/admin/users")[1])
print(f"  · rotated {', '.join(KEEPERS)}")

# The admin's own session died with its stamp; sign back in on the new credential.
admin = Session()
check(admin.login("admin", NEW_PASSWORD), "the rotated admin credential signs in")
if fail:
    print("\nFAILED — rotation locked the rehearsal out of its own admin account")
    sys.exit(1)

for user in RETIRED:
    if user not in id_by_name:
        continue
    admin.post("/admin/users?handler=Toggle", {"id": id_by_name[user]},
               admin.get("/admin/users")[1])
print(f"  · deactivated {', '.join(RETIRED)}")

# ---- step 3: prove it -------------------------------------------------------------
print("\n3. the shared demo password signs in nowhere")
for user in KEEPERS + RETIRED[:3]:
    check(not signs_in(user, DEMO_PASSWORD), f"{user} refuses {DEMO_PASSWORD}")

print("\n   a deactivated account is refused even with a correct password")
# A retired account was never rotated, so DEMO_PASSWORD is still its password on paper — the
# refusal above must come from deactivation, which is the stronger property.
check(not signs_in(RETIRED[0], DEMO_PASSWORD),
      f"{RETIRED[0]} is refused although its password was never changed")

print("\n   /health answers and an operator can still work")
_, health = Session().get("/health")
check('"status":"ok"' in health.replace(" ", ""), f"/health reports ok ({health.strip()[:60]})")
check(signs_in("admin", NEW_PASSWORD), "the rotated credential still signs in after all of it")


# ---- step 4: provisional prices (P8) ----------------------------------------------
def provisional_count(html):
    """The screen only renders the pill when the count is non-zero, so its absence on a page
    that really is masters means zero — not 'could not tell'."""
    m = re.search(r'<span class="pill warn">(\d+) provisional</span>', html)
    if m:
        return int(m.group(1))
    return 0 if "/admin/import" in html else -1


print("\n4. provisional prices are found, priced, and gone (P8)")
_, masters = admin.get("/admin/masters")
before = provisional_count(masters)
check(before >= 0, f"masters states a provisional count ({before})")

# The demo seed deliberately ships one unpriced item (edge 11). Go-live must not merely notice
# it — the operator must be able to clear it, so the rehearsal prices it and re-reads the count.
def unpriced_rows(html):
    """The rows the screen itself marks provisional — not merely the first row with a form."""
    out = []
    for row in html.split("<tr")[1:]:
        if "Provisional</span>" not in row and "No price</span>" not in row:
            continue
        ident = re.search(r'name="TargetId" value="(\d+)"', row)
        kind = re.search(r'name="TargetKind" value="(\w+)"', row)
        if ident and kind:
            out.append((ident.group(1), kind.group(1)))
    return out


for target_id, kind in unpriced_rows(masters):
    admin.post("/admin/masters?handler=Reprice",
               {"TargetId": target_id, "TargetKind": kind,
                "NewPrice": "300", "EffectiveFrom": ""}, masters)
    masters = admin.get("/admin/masters")[1]

check(provisional_count(masters) == 0,
      f"every provisional item can be priced from the screen "
      f"({before} → {provisional_count(masters)})")

print()
if fail:
    print(f"GO-LIVE REHEARSAL FAILED — {len(fail)} step(s) did not hold")
    for f in fail:
        print(f"  - {f}")
    sys.exit(1)
print("GO-LIVE REHEARSAL PASSED — RUNBOOK §9 works as written")
