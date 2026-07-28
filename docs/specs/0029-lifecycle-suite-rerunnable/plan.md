# 0029 — Plan

## Approved: 2026-07-28

## Approach

The finding calls this "cleanup". It is not — nothing else in the remediation can be verified on
an exhausted database, so this is the enabling work and it goes first.

### 1. Shared teardown in `_harness.py`

Three additions, all stdlib, all usable by a legacy thread without adopting its `Session`
(the helpers only ever call `.get()` and `.post()` on whatever object they are handed):

- `fixture(match, what, hint)` — the F4 fix as a function. Returns the match when it is truthy;
  otherwise exits with `FIXTURE EXHAUSTED — <what>. <hint>`. Replaces every bare `.group(1)` on
  a picker regex.
- `on_exit(fn)` — a teardown registry drained by `atexit`, so a thread that dies at step 7 still
  returns the bed it took at step 2. Failures inside teardown are reported, never raised: a
  broken cleanup must not turn a green run red or mask the real error.
- `release_bed(session, bed_id)` / `settle_and_discharge(...)` — the ward-side return path,
  written once. `settle_and_discharge` walks Initiate → Clear → Prepare → Confirm → collect the
  residual due → Discharge, then hands the bed back through `/ipd/board?handler=CleaningDone`.

`BASE` already reads `BASE_URL`. The legacy threads get the same one-line treatment:
`BASE = os.environ.get("BASE_URL", "http://localhost:5199").rstrip("/")`.

### 2. The bed ratchet

| Thread | Today | Change |
|---|---|---|
| `lifecycle-thread.py` | leaves the admission at `financially_settled` | discharge it, release the bed |
| `emr-thread.py` | admits, never discharges | teardown discharges and releases |
| `ot-thread.py` | admits, never discharges | teardown discharges and releases |
| `ipd-thread.py` | discharges, but only on the happy path | teardown, so a mid-thread failure still returns both beds |

`ipd-thread.py` step 10 transfers into a **cabin** specifically. There are three cabins and the
thread needs one free. Widened to "any free bed that is not the one we are in", which is what
the assertion is actually about — a transfer is recorded with its moment in the bed history —
and it stops one thread from monopolising the scarcest class.

### 3. The stock ratchet

- `pharmacy-thread.py` step 4 stops asking for 310 units of seeded Seclo. It asks for
  `sellable + 1` units of **the batch this run received at step 2**, which is the property the
  case is about: the counter refuses a quantity beyond unexpired stock. Self-provisioned, so it
  is true on the first run and the hundredth.
- `ot-thread.py` picks the consumable **by stock on hand** rather than by list position, via a
  pharmacist session reading `/pharmacy/stock`, and fails as an exhausted fixture when nothing
  on the shelf can be consumed. Adds `parvin` to the thread's cast.

### 4. Runner and smoke script

- `lifecycle-suite.py`: when a script exits non-zero and no `✗`/`FAIL` line was found, attach
  the last lines of its output so a traceback is visible in the summary instead of
  `(0 failed check(s))`.
- `nav-smoke.sh`: rewritten around `curl -o /dev/null -w '%{http_code} %{redirect_url}'`.
  A 200 is a pass; a 302 to `/denied` is a **fail**; a 302 to `/login` is a failed login and
  fails the whole batch; anything else fails. Exits with the number of failed routes.

## Risks

- **Teardown that lies.** A cleanup that silently fails re-creates the ratchet while looking
  fixed. Mitigation: the acceptance bar is three consecutive t1 runs on one database, and the
  bed census is checked in SQL after them.
- **`atexit` and `sys.exit`.** `atexit` handlers do run on `SystemExit`, and on an uncaught
  exception, but not on `os._exit` or a signal. No thread uses either.
- **Widening the transfer target** could weaken the assertion. It does not: the check is that
  the bed history records the transfer, and it now names whichever bed was actually chosen.

## Verification

1. `python3 eng/verify/lifecycle-suite.py --tier t1` × 3 on one database, all green.
2. Bed census in SQL before and after: unchanged.
3. Deliberately exhausted database: every failure names its fixture.
4. `nav-smoke.sh` on a refused route: non-zero.
5. `bash eng/check-lifecycle-traceability.sh --stats` green.
