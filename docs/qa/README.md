# QA — how to run the lifecycle suite

`patient-lifecycle.md` is the source of truth. This file is how you execute it.

## Safety tiers

The scripts differ in what they assume about the database, not only in what they cover. Mixing
them is the classic false red.

| Tier | Contains | Writes? | Assumes |
|---|---|---|---|
| **t0** | `role-journeys.py` — 12 roles × 64 protected routes, public surfaces, handler refusals | no | nothing |
| **t1** | `lifecycle-thread`, `edge-cases`, `ipd`, `emr`, `ot`, `radiology`, `pharmacy`, `pharmacy-full`, `frontdesk-check` | yes, its own records | a dirty database is fine |
| **t2** | `golden-thread`, `discount-and-dues` | yes | **a freshly seeded database** |

t2 asserts absolute money totals — `golden-thread.py:206` requires the dashboard to read exactly
৳550 — so it is true only on an empty ledger. It fails on any database that has been used, and
that failure says nothing about the build.

## Running

```sh
# the app must be up; local default is http://localhost:5199
python3 eng/verify/lifecycle-suite.py --tier t0      # read-only, safe anywhere
python3 eng/verify/lifecycle-suite.py --tier t1      # the full lifecycle
python3 eng/verify/lifecycle-suite.py --tier all     # adds t2; needs a fresh DB
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

- Records are named `QA-<runid> …`, findable in `/registration` and the type-ahead.
- A manifest lands in `eng/verify/runs/<host>-<runid>.json` listing every id created.
- Invoices and receipts can be reversed so day-close nets out.

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
