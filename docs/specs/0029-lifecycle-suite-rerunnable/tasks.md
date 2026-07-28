# 0029 — Tasks

## Harness

- [x] `_harness.py`: `fixture(match, what, hint)` — exhausted fixtures fail, never crash
- [x] `_harness.py`: `on_exit(fn)` teardown registry drained by `atexit`, errors reported
- [x] `_harness.py`: `release_bed()` / `settle_and_discharge()` — the ward-side return path
- [x] `BASE_URL` honoured by all nine legacy t1 threads (constant only; no plumbing rewrite)

## The ratchet (F3)

- [x] `lifecycle-thread.py` — discharge the admission it settles, release the bed
- [x] `emr-thread.py` — teardown discharges and releases
- [x] `ot-thread.py` — teardown discharges and releases
- [x] `ipd-thread.py` — teardown covers both beds; transfer targets any free bed, not only a cabin
- [x] `pharmacy-thread.py` — step 4 asserts `sellable + 1` against this run's own batch
- [x] `ot-thread.py` — consumable chosen by stock on hand, via a pharmacist session

## Crash → failure (F4)

- [x] Every picker `.group(1)` behind `fixture()` in the six t1 threads
- [x] `lifecycle-suite.py` attaches a crashed script's tail when no `✗` line was printed

## nav-smoke (F6)

- [x] Follow the redirect; `/denied` is a failure; `/login` fails the batch; exit non-zero

## Verification

- [x] `--tier t1` × 3 consecutive on one database, 12/12 roles, green
- [x] Bed census unchanged across the three runs
- [x] Exhausted-fixture run names the fixture instead of crashing
- [x] `nav-smoke.sh md … /registration /billing/opd` exits non-zero
- [x] `check-lifecycle-traceability.sh --stats` green
