# 0029 — Notes (afterwards)

## What the ratchet actually was

The finding described F3 as "threads consume finite fixtures". The precise mechanism turned out
to be narrower and worse: **cleanup written as the last step of the happy path**. `ipd-thread.py`
had a housekeeping step 14 that handed both beds back correctly — and it never ran, because the
thread had failed at step 2. Every occupied bed in the local database at the start of this spec
belonged to a run that died before reaching its own cleanup.

That is why the fix is `on_exit` rather than a better step 14. Cleanup that only runs when
nothing went wrong is cleanup for the case that did not need it.

`emr-thread.py` and `ot-thread.py` were the simpler version of the same thing: they admitted a
patient because they needed a folio, and simply never discharged. `lifecycle-thread.py`
discharged but never called `CleaningDone`, so its bed sat in Cleaning — not free, not
reportable, and invisible unless you looked at the ward board.

## The trap that cost the most time

**The app on :5199 was serving a different database.** `docs/qa/README.md` warns about a leftover
instance on the port; the instance running here was not leftover, it was current — and its
`ConnectionStrings__Hms` pointed at `hms_upgrade`, a database left behind by an upgrade-gate run.
So the first baseline showed six of nine scripts failing and a bed census that did not match any
query run against `hms`.

`lsof -nP -iTCP:5199` tells you a process is listening. It does not tell you what it is listening
*to*. `ps eww <pid> | tr ' ' '\n' | grep ConnectionStrings` does, and that is the check worth
adding to the habit.

## A defect this spec found rather than fixed

`eng/verify/ui/tests/spec-0020.spec.ts` had the identical bug in the Playwright suite, and it was
subtler: its `releaseAdmission` teardown *did* run in a `finally`, and still leaked, because it
frees the bed by marking the patient absconded — which is only legal from an in-house state, and
the discharge-with-dues case above it settles the folio first. So the teardown ran, was refused,
and reported nothing. Fixed here (walk the discharge with a stated reason when absconding is no
longer legal), because it is the same defect and it was one function.

The general lesson is the one worth keeping: **a teardown that cannot fail loudly is a teardown
you cannot trust**. `_harness.on_exit` prints `!!` and the exception for exactly this reason.

## Deviations

- The plan said `ipd-thread.py`'s transfer would target "any free bed that is not the one we are
  in", preferring a different class. Implemented as written; the assertion now names whichever
  bed was chosen rather than asserting `CAB-`. This is not weaker: the case is that the bed
  history records the move with its moment, and it now does so for whatever bed was used.
- `pharmacy-thread.py` step 4 needed a named customer, which the plan did not anticipate. A
  walk-in with nothing tendered is refused earlier — for credit without a name — and would never
  have reached the stock check under test. The case now bills a named patient so the refusal it
  asserts is the one it means.

## Verified

- `--tier t1` green **three consecutive times** on one database, 12/12 roles, bed census 13/13
  free before and after.
- `--tier t0` green, `--tier t2` green on a fresh database.
- `dotnet build -c Release`: 0 warnings, 0 errors. 154 xUnit tests pass.
- `nav-smoke.sh md 'Demo#1234' /registration /billing/opd` → exit 1, both routes reported
  `DENIED`. The same script on a route the user holds → exit 0.
- Playwright: 245 tests, all pass once `/ot/case/1`'s data prerequisite is met (see below).

## Follow-up

`/ot/case/1` joined the Playwright `ROUTES` table in spec 0031, and no seeded database has an
operation on it — only `ot-thread.py` creates one. `eng/verify/ui/README.md`'s prerequisite list
now names it alongside `golden-thread.py` and `discount-and-dues.py`. A seeded demo operation
would be the better answer, and belongs to whoever next touches the seed.
