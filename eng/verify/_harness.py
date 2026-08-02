#!/usr/bin/env python3
"""Shared plumbing for every verification script in this directory.

Before this module each script carried its own copy of the same `Session` class, its own
`check`/`step` helpers, and — in nine of thirteen cases — a hardcoded `http://localhost:5199`
that made the deployment untestable. All of that lives here now.

Three things this adds beyond deduplication:

  * `guard(tier)` — the environment interlock. Anything that is not localhost is treated as a
    real deployment and refuses to be written to without an explicit `--env` and a typed
    confirmation. Rule 4 (no financial hard deletes) means a mutating run against production
    leaves rows that can never be removed, only reversed, so the default must be refusal.
  * `case(id, ...)` — lifecycle case IDs (`LC-<STAGE>-<nn>`) that tie a running assertion back
    to a row in `docs/qa/patient-lifecycle.md`. `eng/check-lifecycle-traceability.sh` joins the
    two and fails the build when they drift.
  * role tracking — every `Session` records the user it logged in as, so a run can assert it
    actually exercised all twelve roles instead of reusing one convenient privileged session.

Stdlib only, on purpose: no new dependency for a 2 vCPU / 3 GB box (G15).
"""
import atexit
import http.cookiejar
import json
import os
import pathlib
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")
TOKEN_RE = re.compile(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"')
DEV_PASSWORD = "Demo#1234"          # DevSeed.DevPassword — the demo card password (07 §1)

# One id for a whole suite run, not one per script: the runner exports HMS_QA_RUN_ID so all
# ten threads file their manifests under the same directory. A per-process id would scatter a
# single run across ten timestamps and make it unreversible in practice.
RUN_ID = os.environ.get("HMS_QA_RUN_ID") or time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
RUNS_DIR = pathlib.Path(__file__).resolve().parent / "runs"

# ---------------------------------------------------------------------------
# The cast. Mirrors DevSeed.cs `Cast` — the traceability guard asserts it still matches.
# ---------------------------------------------------------------------------
CAST = {
    "jashim":    "Receptionist",
    "rasel":     "Billing Operator",
    "ripon":     "Lab Technologist",
    "farhana":   "Pathologist",
    "shahid":    "Billing Supervisor",
    "admin":     "Admin",
    "md":        "MD",
    "parvin":    "Pharmacist",
    "nasrin":    "Nurse",
    "chowdhury": "OPD Consultant",
    "shaheen":   "OT In-charge",
    "moinul":    "Radiology Technician",
}

USERS_SEEN: set[str] = set()        # every username a run logged in as
MANIFEST: dict[str, list] = {}      # entity kind -> ids created, for reversal on a real target


# ---------------------------------------------------------------------------
# Environment interlock
# ---------------------------------------------------------------------------
def is_local(url: str = BASE) -> bool:
    host = urllib.parse.urlparse(url).hostname or ""
    return host in ("localhost", "127.0.0.1", "::1", "0.0.0.0")


def guard(tier: str = "t1", env: str | None = None) -> None:
    """Refuse to mutate a target that is not demonstrably local without explicit consent.

    tier: "t0" (read-only) may run anywhere unattended. "t1" (mutating, own data) and "t2"
    (mutating, needs a fresh database) may not.
    """
    env = env or os.environ.get("HMS_QA_ENV", "")
    confirmed = os.environ.get("HMS_QA_CONFIRM", "")

    if tier == "t0" or is_local():
        if tier == "t2" and not is_local():
            sys.exit("REFUSED: tier t2 rewrites absolute totals and is fresh-database only. "
                     f"Target {BASE} is not local.")
        return

    # Non-local target, mutating tier.
    if env not in ("vm", "prod"):
        sys.exit(f"REFUSED: {BASE} is not localhost. A mutating run against a real deployment "
                 "needs an explicit environment: HMS_QA_ENV=vm or HMS_QA_ENV=prod.")
    if env == "prod" and tier == "t2":
        sys.exit("REFUSED: tier t2 is never permitted against production — it asserts absolute "
                 "money totals and assumes a fresh database.")
    if confirmed != urllib.parse.urlparse(BASE).hostname:
        sys.exit(f"REFUSED: writing to {env} target {BASE} requires confirmation. Set "
                 f"HMS_QA_CONFIRM={urllib.parse.urlparse(BASE).hostname} once you have read "
                 "docs/qa/README.md on what a mutating run leaves behind.")
    print(f"  ! mutating run against {env} target {BASE} — records tagged QA-{RUN_ID}")


def tag(name: str) -> str:
    """Name a created record so it is findable later on a shared target."""
    return f"QA-{RUN_ID} {name}" if not is_local() else name


def record(kind: str, value) -> None:
    MANIFEST.setdefault(kind, []).append(value)


def write_manifest() -> pathlib.Path | None:
    """One file per script, all under one run directory — the trail back to what a run created.

    Local runs write nothing: the manifest exists so a run against a *shared* target can be
    found and reversed, and on a laptop the database is disposable.
    """
    if is_local() or not MANIFEST:
        return None
    host = urllib.parse.urlparse(BASE).hostname
    run_dir = RUNS_DIR / f"{host}-{RUN_ID}"
    run_dir.mkdir(parents=True, exist_ok=True)
    path = run_dir / f"{pathlib.Path(sys.argv[0]).stem or 'run'}.json"
    path.write_text(json.dumps(
        {"run": RUN_ID, "base": BASE, "script": pathlib.Path(sys.argv[0]).name,
         "finished": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
         "created": MANIFEST}, indent=2))
    return path


# ---------------------------------------------------------------------------
# HTTP session
# ---------------------------------------------------------------------------
class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *_args, **_kwargs):
        return None


class Session:
    """One operator's browser: cookie jar, antiforgery token, form posts."""

    def __init__(self, user: str, password: str = DEV_PASSWORD):
        self.user = user
        self.role = CAST.get(user, "?")
        jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
        self.plain = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(jar), _NoRedirect)
        self.post("/login", {"Username": user, "Password": password}, self.get("/login"))
        USERS_SEEN.add(user)

    def get(self, path: str) -> str:
        with self.op.open(BASE + path) as r:
            return r.read().decode()

    def status(self, path: str) -> tuple[int, str]:
        """Status and Location without following redirects — how denial is observed."""
        try:
            with self.plain.open(BASE + path) as r:
                return r.getcode(), r.headers.get("Location", "")
        except urllib.error.HTTPError as e:
            return e.code, e.headers.get("Location", "")

    def denied(self, path: str) -> bool:
        """True when the server refuses this route for this user.

        Deny-by-default sends 302 to /denied rather than 403, so a bare status check cannot
        tell a refusal from an ordinary redirect — which is why `nav-smoke.sh` treating 302 as
        success hides exactly the failures an authorization test exists to catch.
        """
        code, location = self.status(path)
        if code in (401, 403):
            return True
        return code in (301, 302) and "/denied" in location

    def post(self, path: str, fields: dict, page_html: str | None = None) -> tuple[str, str]:
        html = page_html if page_html is not None else self.get(path)
        m = TOKEN_RE.search(html)
        data = dict(fields)
        if m:
            data["__RequestVerificationToken"] = m.group(1)
        req = urllib.request.Request(
            BASE + path, data=urllib.parse.urlencode(data, doseq=True).encode())
        with self.op.open(req) as r:
            return r.geturl(), r.read().decode()

    def post_denied(self, path: str, fields: dict) -> bool:
        """Whether a state-changing POST is refused — the handler-level bypass check.

        Hiding a button is never the control (G10); the handler itself must refuse.
        """
        try:
            url, _ = self.post(path, fields)
            return "/denied" in url or "/login" in url
        except urllib.error.HTTPError as e:
            return e.code in (400, 401, 403)


# ---------------------------------------------------------------------------
# Reporting — case ids tie back to docs/qa/patient-lifecycle.md
# ---------------------------------------------------------------------------
RESULTS: list[dict] = []
_current: dict | None = None


def case(lc_id: str, title: str, actor: str | Session | None = None):
    """Open a lifecycle case. `actor` is the demo user performing it."""
    global _current
    who = actor.user if isinstance(actor, Session) else (actor or "")
    _current = {"id": lc_id, "title": title, "actor": who, "checks": [], "failed": []}
    RESULTS.append(_current)
    label = f"[{who}/{CAST.get(who, '?')}]" if who else ""
    print(f"\n{lc_id}  {title} {label}")
    return _current


def step(n, text: str) -> None:
    print(f"  {n:2}. {text}")


def check(cond, msg: str) -> bool:
    ok = bool(cond)
    print(f"      {'PASS' if ok else 'FAIL'}  {msg}")
    if _current is not None:
        _current["checks"].append(msg)
        if not ok:
            _current["failed"].append(msg)
    else:
        _LOOSE.append((ok, msg))
    return ok


_LOOSE: list[tuple[bool, str]] = []


def failures() -> list[str]:
    out = [m for _, r in enumerate(RESULTS) for m in r["failed"]]
    out += [m for ok, m in _LOOSE if not ok]
    return out


def roles_missing() -> list[str]:
    return sorted(set(CAST) - USERS_SEEN)


def report(title: str, require_all_roles: bool = False) -> int:
    """Print the run summary and return the process exit code."""
    bad = failures()
    print("\n" + "=" * 78)
    print(f"{title}: {len(RESULTS)} cases, {len(bad)} failed check(s)")
    if USERS_SEEN:
        print(f"roles exercised: {len(USERS_SEEN)}/{len(CAST)} — "
              f"{', '.join(sorted(USERS_SEEN))}")
    missing = roles_missing()
    if require_all_roles and missing:
        print(f"INCOMPLETE: never logged in as {', '.join(missing)}")
        bad = bad + [f"role never exercised: {u}" for u in missing]
    for m in bad:
        print(f"  FAIL  {m}")
    path = write_manifest()
    if path:
        print(f"manifest: {path}")
    if os.environ.get("HMS_QA_JSON"):
        pathlib.Path(os.environ["HMS_QA_JSON"]).write_text(json.dumps(
            {"title": title, "base": BASE, "cases": RESULTS,
             "users": sorted(USERS_SEEN), "failed": bad}, indent=2))
    print("=" * 78)
    return 1 if bad else 0


# ---------------------------------------------------------------------------
# Domain helpers used by more than one script
# ---------------------------------------------------------------------------
def grant_cells(html: str) -> list[tuple[str, str, str, bool]]:
    """`(role, permission, role_id, held)` for every cell of `/admin/users`' §12 matrix.

    The matrix is the only place a deployment's grants can be read without a new endpoint, and
    the four values of a cell sit in four separate tags. Matching them one at a time across a
    character window picks up the *neighbouring* role's id — the cells are adjacent — so the
    whole tuple is matched in one pass or not at all.
    """
    out = []
    for m in re.finditer(
            r'name="roleId" value="(\d+)"\s*/>\s*'
            r'<input type="hidden" name="permission" value="([a-z][a-z.]+)"\s*/>\s*'
            r'<input type="hidden" name="grant" value="(?:true|false)"\s*/>[\s\S]{0,200}?'
            r'title="(Revoke|Grant) [a-z.]+ for ([^"]+)"', html):
        role_id, perm, verb, role = m.groups()
        out.append((role, perm, role_id, verb == "Revoke"))
    return out


def fixture(match, what: str, hint: str = "reset the database — see docs/qa/README.md"):
    """Return `match`, or stop the run naming the fixture that ran out.

    Spec 0029 F4. Every thread picks its fixtures out of the rendered page with a regex —
    a free bed, a sellable batch, a priced operation. When the fixture is gone the match is
    `None`, and `.group(1)` on it raises `AttributeError: 'NoneType' object has no attribute
    'group'`, which tells the operator nothing about what broke or what to do. This says both.

    The leading marker is what `lifecycle-suite.py` scrapes for, so an exhausted fixture shows
    up in the suite summary as a named failure rather than an empty one.
    """
    if match:
        return match
    print(f"      ✗ FIXTURE EXHAUSTED — {what}")
    print(f"        {hint}")
    sys.exit(2)


# ---------------------------------------------------------------------------
# Teardown — a thread returns what it takes (spec 0029 F3)
# ---------------------------------------------------------------------------
_TEARDOWNS: list[tuple[str, object]] = []


def on_exit(what: str, fn) -> None:
    """Register cleanup that must happen even when the thread dies at the next step.

    The bed ratchet that made the suite un-rerunnable came from cleanup written as the last
    step of the happy path: a thread that failed at step 7 never reached the step that handed
    its bed back, so every failed run cost the ward a bed permanently.
    """
    _TEARDOWNS.append((what, fn))


@atexit.register
def _file_manifest() -> None:
    """Every script files its manifest, including the nine that never call `report()`.

    The legacy threads print their own summary and `sys.exit` — they never reach the harness's
    reporting path, which is why `write_manifest()` had a caller in theory and none in practice
    (round-2 finding R2-3). Filing it at exit needs no cooperation from the script.
    """
    try:
        path = write_manifest()
        if path:
            print(f"  manifest: {path}")
    except Exception as e:                          # noqa: BLE001 — never mask the real result
        print(f"  !! manifest not written — {type(e).__name__}: {e}")


@atexit.register
def _drain_teardowns() -> None:
    if not _TEARDOWNS:
        return
    print("\n  teardown — returning the fixtures this run took")
    while _TEARDOWNS:
        what, fn = _TEARDOWNS.pop()
        try:
            fn()
            print(f"      ok  {what}")
        except Exception as e:                      # noqa: BLE001 — teardown never re-raises
            # A broken cleanup must not turn a green run red, nor mask the real failure.
            print(f"      !!  {what} — {type(e).__name__}: {e}")


def release_bed(sess, bed_id) -> None:
    """Hand a bed in Cleaning back to the ward (§11 — real housekeeping does this)."""
    board = sess.get("/ipd/board")
    sess.post("/ipd/board?handler=CleaningDone", {"BedId": str(bed_id)}, board)


def settle_and_discharge(make, admission_id, bed_ids=()) -> None:
    """Walk an open admission to `discharged` and give its bed(s) back.

    `make(username)` returns a logged-in session — each legacy thread still owns its own
    `Session` class, so the helper is handed a factory rather than constructing one.

    The walk is the same one `ipd-thread.py` performs on its happy path: summary → clinical
    clearance → settlement draft → invoice → discharge. `OutstandingReason` is always supplied
    because teardown must not be blocked by a residual due; that reason lands in the tier-2
    audit trail exactly as §3.2 requires, which is the correct outcome — a bed released by QA
    should be as attributable as one released by a person.
    """
    fd = make("jashim")                     # ipd.manage — summary, clearance, discharge
    op = make("rasel")                      # ipd.settle — draft, invoice, discharge

    # An R4 hold has to come off first: a blocked admission cannot be discharged, and the
    # release is approval-gated both ways by design. Found on the deployment, where a thread
    # that raised a block left GWF-04 held indefinitely — the teardown ran, reported success,
    # and had silently done nothing.
    if "Blocked for dues" in fd.get(f"/ipd/folio/{admission_id}"):
        blocked = fd.get("/ipd/admissions?Tab=blocked")
        fd.post("/ipd/admissions?handler=RaiseRelease",
                {"AdmissionId": admission_id,
                 "Reason": "QA teardown — releasing the hold to return the bed"}, blocked)
        sup = make("shahid")                # admin.approvals.decide
        inbox = sup.get("/admin/approvals")
        pending = re.search(r'Patient-release.*?name="id" value="(\d+)"', inbox, re.S)
        if pending:
            sup.post("/admin/approvals?handler=Decide",
                     {"id": pending.group(1), "approve": "true", "note": "QA teardown"}, inbox)
        blocked = fd.get("/ipd/admissions?Tab=blocked")
        approved = re.search(
            r'name="AdmissionId" value="' + str(admission_id) +
            r'"\s*/>\s*<input type="hidden" name="ApprovalId" value="(\d+)"', blocked)
        if approved:
            fd.post("/ipd/admissions?handler=ApplyRelease",
                    {"AdmissionId": admission_id, "ApprovalId": approved.group(1)}, blocked)

    page = fd.get(f"/ipd/discharge/{admission_id}")

    if "Gate pass" not in page:
        if "Clinical summary" in page or "Start discharge" in page:
            fd.post(f"/ipd/discharge/{admission_id}?handler=Initiate",
                    {"AdmissionId": admission_id,
                     "ClinicalSummary": "QA teardown — returning the bed to the ward."}, page)
        page = fd.get(f"/ipd/discharge/{admission_id}")
        fd.post(f"/ipd/discharge/{admission_id}?handler=Clear",
                {"AdmissionId": admission_id}, page)

        open_counter(op)
        page = op.get(f"/ipd/discharge/{admission_id}")
        op.post(f"/ipd/discharge/{admission_id}?handler=Prepare",
                {"AdmissionId": admission_id}, page)
        page = op.get(f"/ipd/discharge/{admission_id}")
        op.post(f"/ipd/discharge/{admission_id}?handler=Confirm",
                {"AdmissionId": admission_id, "DiscountFlat": "0"}, page)

        page = op.get(f"/ipd/discharge/{admission_id}")
        op.post(f"/ipd/discharge/{admission_id}?handler=Discharge",
                {"AdmissionId": admission_id,
                 "OutstandingReason": "QA teardown — bed returned to the ward"}, page)

    for bed_id in {str(b) for b in bed_ids if b}:
        release_bed(fd, bed_id)


def open_counter(sess: Session, kind: str = "Front Desk", float_amt: str = "2000") -> None:
    page = sess.get("/billing/session")
    if "Your counter is open" in page:
        return
    m = re.search(r'value="(\d+)"[^>]*>[^<]*' + kind, page)
    sess.post("/billing/session",
              {"CounterId": m.group(1) if m else "1", "OpeningFloat": float_amt}, page)


def find_patient(sess: Session, term: str) -> str | None:
    hits = json.loads(sess.get("/api/typeahead/patients?q=" + urllib.parse.quote(term)))
    return str(hits[0]["value"]) if hits else None


def _sellable_demo_qty(stock_html: str) -> int:
    """Sellable units of the demo product on the stock page (main outlet is the default view)."""
    on_hand = 0
    for row in re.findall(r"<tr>(.*?)</tr>", stock_html, re.S):
        cells = [re.sub(r"<[^>]+>", "", c).strip()
                 for c in re.findall(r"<td[^>]*>(.*?)</td>", row, re.S)]
        if len(cells) >= 7 and "Napa" in cells[0]:
            state = cells[6].split("\n")[0].strip()
            if state in ("in stock", "near expiry"):
                on_hand += int(re.sub(r"\D", "", cells[3]) or 0)
    return on_hand


def ensure_demo_stock(make, min_qty: int = 15, receive_qty: int = 100) -> None:
    """Guarantee the seeded demo product is on the main-outlet shelf before a run consumes it.

    Tier-1 scripts provision their own patients (that is t1's contract), but the ward threads
    issue the seeded product from the shared shelf and nothing puts stock back: on a heavily
    used database the shelf eventually reads zero and the FEFO issue is — correctly — refused,
    so the run goes red on state it inherited, not code it exercised. The suite's second
    consecutive used-database run found exactly that (spec 0039). Same class of fix as
    pharmacy-thread's per-run GRN: a run creates the stock it consumes.

    No-op while at least `min_qty` sellable units remain. Replenishment is the operator path
    (PO → approve⚿ → order → GRN), not a database write, so it exercises the same controls it
    relies on. `make(username)` returns a logged-in session — the threads own their `Session`
    classes, so the helper takes a factory, like `settle_and_discharge`.
    """
    ph = make("parvin")                                      # pharmacy: PO, GRN
    if _sellable_demo_qty(ph.get("/pharmacy/stock?Show=all")) >= min_qty:
        return

    pur = ph.get("/pharmacy/purchase")
    prod = fixture(re.search(r'<option value="(\d+)">Napa', pur),
                   "no demo product on the purchase-order form")
    sup_opt = fixture(re.search(r'name="SupplierId">.*?<option value="(\d+)">(?!— )', pur, re.S),
                      "no supplier to raise the replenishment order against")
    out_opt = fixture(re.search(r'name="OutletId">.*?<option value="(\d+)">(?!— )', pur, re.S),
                      "no pharmacy outlet to receive the replenishment into")

    sup = make("shahid")                                     # supervisor: PO approval
    before = set(re.findall(r'name="id" value="(\d+)"', sup.get("/admin/approvals")))
    ph.post("/pharmacy/purchase?handler=Create", {
        "SupplierId": sup_opt.group(1), "OutletId": out_opt.group(1),
        "ProductIds": [prod.group(1), "0", "0"], "LineQtys": [str(receive_qty), "0", "0"],
        "LineCosts": ["1", "0", "0"]}, pur)
    pur = ph.get("/pharmacy/purchase")                       # newest-first: ours is on top
    po_id = fixture(re.search(r"PO #(\d+)", pur),
                    "the replenishment purchase order was not created").group(1)

    inbox = sup.get("/admin/approvals")
    mine = sorted(set(re.findall(r'name="id" value="(\d+)"', inbox)) - before, key=int)
    rid = fixture(mine[-1] if mine else None,
                  "the replenishment PO never reached the approvals inbox")
    sup.post("/admin/approvals?handler=Decide",
             {"id": rid, "approve": "true", "note": "QA stock replenishment"}, inbox)

    for _ in range(2):                                       # Requested → Approved → Ordered
        pur = ph.get("/pharmacy/purchase")
        ph.post("/pharmacy/purchase?handler=Advance", {"PoId": po_id}, pur)
    pur = ph.get("/pharmacy/purchase")
    line = fixture(re.search(r'name="PoId" value="' + po_id +
                             r'"\s*/>\s*<input type="hidden" name="PoLineId" value="(\d+)"',
                             pur, re.S),
                   "the ordered replenishment PO offers no line to receive against")
    ph.post("/pharmacy/purchase?handler=Receive", {
        "PoId": po_id, "PoLineId": line.group(1),
        "BatchNo": f"SUITE-{int(time.time() * 1000) % 100000:05d}",
        "Expiry": "31/12/2030", "Qty": str(receive_qty),
        "UnitCost": "1", "UnitMrp": "2"}, pur)

    fixture(_sellable_demo_qty(ph.get("/pharmacy/stock?Show=all")) >= min_qty,
            "the replenishment GRN did not put the demo product on the shelf",
            "walk /pharmacy/purchase by hand — a gate or approval change has broken the PO path")
