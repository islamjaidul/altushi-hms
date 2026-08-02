# QA — how to run the lifecycle suite

`patient-lifecycle.md` is the source of truth. This file is how you execute it.

## Safety tiers

The scripts differ in what they assume about the database, not only in what they cover. Mixing
them is the classic false red.

| Tier | Contains | Writes? | Assumes |
|---|---|---|---|
| **t0** | `role-journeys.py` — 12 roles × 64 protected routes, public surfaces, handler refusals · `grant-drift.py` — the deployment's §12 matrix vs the code's | no | nothing |
| **t1** | `lifecycle-thread`, `edge-cases`, `ipd`, `emr`, `ot`, `radiology`, `pharmacy`, `pharmacy-full`, `frontdesk-check`, `money-and-controls` | yes, its own records | a dirty database is fine |
| **t2** | `golden-thread`, `discount-and-dues` | yes | **a freshly seeded database** |

t2 asserts absolute money totals — `golden-thread.py:206` requires the dashboard to read exactly
৳550 — so it is true only on an empty ledger. It fails on any database that has been used, and
that failure says nothing about the build.

**`--tier all` therefore runs t0 → t2 → t1, not t0 → t1 → t2.** t2 has to meet the fresh ledger
before the ten mutating t1 scripts spend it; t0 is read-only and stays first, where a broken
login fails the run in seconds. Until spec 0032 the runner executed the tiers in numeric order,
which made the documented `--tier all` run red by construction — the runner destroyed the one
precondition this page tells you to provide. An explicit `--tier t0|t1|t2` is unchanged.

## Running

```sh
# the app must be up; local default is http://localhost:5199
python3 eng/verify/lifecycle-suite.py --tier t0      # read-only, safe anywhere
python3 eng/verify/lifecycle-suite.py --tier t1      # the full lifecycle
python3 eng/verify/lifecycle-suite.py --tier all     # t0 → t2 → t1; needs a fresh DB
```

A t1 run that never logs in as all twelve demo users fails as **incomplete**. That is
deliberate: a lifecycle driven by one convenient privileged session proves nothing about the
operators who actually perform the work.

Fresh database, when t2 is wanted:

```sh
docker exec hms-dev-db psql -U postgres -d postgres \
  -c "DROP DATABASE IF EXISTS hms WITH (FORCE);" -c "CREATE DATABASE hms;"
```

## Against a deployment

Anything that is not localhost is treated as a real deployment. Read-only probing needs nothing:

```sh
BASE_URL=https://hms.example.com python3 eng/verify/lifecycle-suite.py --tier t0
```

A **mutating** run needs the environment named and the host typed back:

```sh
BASE_URL=https://hms.example.com HMS_QA_ENV=prod HMS_QA_CONFIRM=hms.example.com \
  python3 eng/verify/lifecycle-suite.py --tier t1
```

t2 against `prod` is refused unconditionally.

### What a mutating production run leaves behind

Rule 4 forbids financial hard deletes, so a production run is designed to be **identifiable and
reversible, never erasable**.

- Records are named by the thread that created them plus a millisecond stamp — `Lifecycle 51816`,
  `Edge Death 07211`, `OT Test 08033`, `Reprice Probe 80243` — findable in `/registration` and
  the type-ahead. They are also bounded by the run's timestamp window.
- Invoices and receipts can be reversed so day-close nets out.
- The ward census is asserted: a run that ends with fewer free beds than it started with **fails**,
  so a leaked admission cannot hide behind a green result.

On a **non-local** target the records a run creates are named `QA-<runid> …` and every script
files a manifest under `eng/verify/runs/<host>-<runid>/<script>.json` listing the ids it created
(patients, admissions, invoices, OT cases). One run id covers the whole suite, so one directory
is one run. Locally neither happens: the prefix would only clutter a database you are about to
throw away.

**Cannot be undone, by design:** `kernel.audit_event` rows, `ipd.bed_day` rows,
`pharm.stock_move` ledger entries, consumed number-series values, and any SMS actually sent. A
production t1 run permanently adds audit and ledger history. Run it before counters open, or
accept a dashboard blip until the reversal completes.

The demo cast (`Demo#1234`) is what the suite authenticates as. Once RUNBOOK §9's go-live switch
is executed those users are gone and t1 against production stops working — by design.

## Traceability

```sh
bash eng/check-lifecycle-traceability.sh --stats
```

Fails when the document cites a script that does not exist, names a user absent from the seeded
cast, declares a permission nothing enforces, or when the role-journey route table drifts from
the `[Authorize]` attributes on the page models. That last check is the important one: the
Playwright tables in `eng/verify/ui/helpers/users.ts` are hand-synced with three C# files and
drift silently today.

## Two traps

Both documented in spec 0023's notes, both cost an hour the first time:

- **A leftover app instance on the port silently serves the wrong database.** Check
  `lsof -nP -iTCP:5199 -sTCP:LISTEN` before believing a result.
- **The Razor build server goes stale after `.cshtml` edits**, producing phantom errors in files
  nobody touched. `dotnet build-server shutdown`.
