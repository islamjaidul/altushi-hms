# 0029 — The lifecycle suite must survive being run twice

- **Status:** Done
- **Date:** 2026-07-28
- **PRD ref:** §8 (N-series, verifiability), §14
- **MVP:** in scope — no new product behaviour. This is test-harness work that makes spec 0028's
  suite a regression net rather than a one-shot report.

## Problem

`docs/qa/findings-2026-07-28.md` F3, F4 and F6. Spec 0028 shipped a suite that passed once and
degraded from there.

1. **F3 — threads consume finite fixtures and never return them.** A hospital has thirteen
   seeded beds. `emr-thread.py` and `ot-thread.py` admit a patient and never discharge;
   `lifecycle-thread.py` settles its admission financially but never walks it to `discharged`,
   so the bed stays held. Observed on the local development database at the start of this spec:

   ```
   id | state                | bed    | patient
    4 | admitted             | CAB-01 | EMR Test 07211
    5 | admitted             | CAB-02 | OT Test 08033
   15 | admitted             | CAB-03 | EMR Test 21253
   16 | admitted             | GWF-01 | OT Test 21786
   23 | financially_settled  | GWF-02 | Spec0020 51816
   27 | financially_settled  | GWF-03 | Spec0020 42727
   30 | financially_settled  | GWF-04 | Spec0020 71568
   ```

   Seven of thirteen beds held by test runs that ended hours earlier. On the `hms_upgrade`
   database the ratchet had gone all the way: **thirteen of thirteen**. Six of nine t1 scripts
   then fail, and none of the failures says anything about the build.

   The same shape applies to pharmacy stock: `pharmacy-thread.py` step 4 asks for 310 units of a
   seeded product to prove that a sale beyond sellable stock is refused, and `ot-thread.py`
   consumes two units of whichever product the picker lists first. Both draw down a seeded
   fixture that nothing replenishes.

2. **F4 — an exhausted fixture crashes instead of failing.** `pharmacy-thread.py:140` calls
   `.group(1)` on a `None` regex match and dies with
   `AttributeError: 'NoneType' object has no attribute 'group'`. The operator-facing message
   names neither the fixture nor the remedy. `lifecycle-thread.py:134` and `ipd-thread.py`
   have the identical defect on the bed picker, and `edge-cases.py:131` raises a bare
   `HTTPError: 404` for the same reason.

3. **F4 (runner half) — the runner hides crashes.** `lifecycle-suite.py` scrapes failed checks
   out of a script's stdout by looking for `✗`/`FAIL`. A script that dies on a traceback prints
   neither, so the runner reports `FAIL exit=1 (0 failed check(s))` and shows an empty failure
   list. Two of the six failures above were invisible for exactly this reason.

4. **F6 — `nav-smoke.sh` cannot fail.** No exit-code logic, and it treats `302` as acceptable.
   Denial in this app **is** a 302, to `/denied`. The script returns 0 when every route it
   probes was refused, and the CI `upgrade-path` job runs it across thirteen user/route batches.

5. **The legacy threads cannot reach a deployment.** Nine of them hardcode
   `http://localhost:5199`, so `--tier t1` against the VM silently tests localhost. Spec 0028's
   notes recorded this; the interlocks in `_harness.py` guard a tier that could not travel.

## Requirements

- **[M]** Every t1 thread returns the fixtures it takes. A thread that admits a patient
  discharges them and hands the bed back to the ward, **on every exit path including failure**.
- **[M]** A thread that draws pharmacy stock either creates that stock itself or asserts a
  property against a batch it created — never against a seeded quantity.
- **[M]** `python3 eng/verify/lifecycle-suite.py --tier t1` passes **three consecutive times on
  one database**. That was spec 0020's bar and it is this one's.
- **[M]** A genuinely exhausted fixture fails with `fixture exhausted — reset the database`,
  naming the fixture. No `AttributeError`, no bare `HTTPError`.
- **[M]** `lifecycle-suite.py` surfaces a crashed script's traceback in its summary.
- **[M]** `nav-smoke.sh` follows the redirect, treats a `/denied` target as a failure, and exits
  non-zero. It reuses the distinction `Session.denied()` already makes.
- **[M]** Every t1 thread honours `BASE_URL`, so the suite can be pointed at the VM.
  Filenames and CLI are unchanged — `.github/workflows/ci.yml` invokes nine of them by name.
- **[S]** The teardown is shared, not copy-pasted into each thread.

## Out of scope

- Retro-fitting the legacy threads onto `_harness.py`'s `Session`. `BASE_URL` support is added
  by making the constant read the environment; the plumbing rewrite stays deferred, for the
  reason spec 0028's notes give — a refactor in the same pass as a fixture fix makes a
  regression untraceable.
- `golden-thread.py`'s absolute-total assertion. It is correct, and t2 exists for it.

## Acceptance

- Three consecutive green `--tier t1` runs on one database, with all twelve roles exercised.
- On a deliberately exhausted database, every failure names its fixture and the remedy.
- `bash eng/verify/nav-smoke.sh md 'Demo#1234' /registration /billing/opd` exits **non-zero**
  and says which routes were refused.
- `BASE_URL=… --tier t1` reaches the named host (verified against the VM).
