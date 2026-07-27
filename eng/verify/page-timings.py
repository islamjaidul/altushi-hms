#!/usr/bin/env python3
"""Spec 0023: median page timings over a loaded database.

Split out of `measure-rss.sh` because signing in is a page interaction, not a curl one-liner —
the bash version timed redirects to /login and reported them as page times. This reuses the
same session helper every other verification script uses, so it fails loudly instead of
silently measuring the wrong thing.

usage: python3 eng/verify/page-timings.py [base_url]
"""
import http.cookiejar
import re
import sys
import time
import urllib.parse
import urllib.request

BASE = sys.argv[1] if len(sys.argv) > 1 else "http://localhost:5199"
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')

# (path, the user who is allowed to see it) — §12 means no single account opens every screen.
PAGES = [
    ("/billing/dues", "rasel"),
    ("/billing/reports", "rasel"),
    ("/registration", "jashim"),
    ("/admin/audit", "admin"),
    ("/dashboard", "md"),
    ("/pharmacy/stock", "parvin"),
    ("/ipd/board", "jashim"),
]
PASSWORD = "Demo#1234"


class Session:
    def __init__(self, user):
        self.op = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()))
        html = self.get("/login")[1]
        token = TOKEN_RE.search(html)
        data = {"Username": user, "Password": PASSWORD}
        if token:
            data["__RequestVerificationToken"] = token.group(1)
        req = urllib.request.Request(BASE + "/login", data=urllib.parse.urlencode(data).encode())
        with self.op.open(req) as r:
            if "/login" in r.geturl():
                raise SystemExit(f"could not sign in as {user} — timings would be meaningless")

    def get(self, path):
        with self.op.open(BASE + path) as r:
            return r.geturl(), r.read().decode()


sessions = {}
worst = 0.0
print("   page                 median   slowest of 5   (5 samples each)")
for path, user in PAGES:
    if user not in sessions:
        sessions[user] = Session(user)
    s = sessions[user]
    samples = []
    for _ in range(5):
        started = time.perf_counter()
        url, _body = s.get(path)
        samples.append(time.perf_counter() - started)
        if "/login" in url or "/denied" in url:
            raise SystemExit(f"{path} redirected to {url} — not a page")
    samples.sort()
    median, slowest = samples[2], samples[-1]
    worst = max(worst, slowest)
    print(f"   {path:<20} {median * 1000:6.0f}ms   {slowest * 1000:6.0f}ms")

# §8 N1 asks for a perceived billing response inside a second. These are server timings on a
# loaded database, not end-to-end perceived times, so the bar here is deliberately stricter.
print(f"\n   slowest single response: {worst * 1000:.0f}ms")
if worst > 1.0:
    print("   !! a page took over a second on this data — investigate before shipping more modules")
    sys.exit(1)
