# 0023 — Plan

## Approved: 2026-07-27

## 1. History generator (`src/Hms.Web/HistoryGenerator.cs`)

Invoked from `Program.cs` when `args` starts with `generate-history`: the web host is built (so
DI, contexts and services are the production wiring) but `app.Run()` is never reached — the
generator runs and the process exits with 0/1.

Per simulated day, in one `HmsTx` transaction per business action (G19 — the generator is not
allowed a shortcut the app doesn't have):

| Volume/day | §14 typical | Generated |
|---|---|---|
| Registrations | 80–150 | 100 |
| OPD invoices | 150–350 | 220 |
| Diagnostic orders | 150–350 | 90 of the invoices carry tests |
| Samples/results | 400–1,000 | ~250 (2–4 tests per order, verified on ~85%) |
| Pharmacy sales | 150–400 | 160 |
| Admissions | 10–25 | 12 admitted, ~11 discharged after 2–5 days |
| Counter sessions | 4–8 | 3 opened and closed per day |

Dates are back-dated by writing the entity's timestamp fields after creation (the services stamp
`now`; the generator rewrites `at`/`created_at` inside the same transaction). This is the one
place in the product allowed to do that, and it is why the command is not a config flag.

Determinism: a fixed-seed `Random` so two runs over the same day range produce the same shape.
Idempotency: a marker row in `kernel.app_setting` (`history.generated.through`) records the last
generated date; re-running only fills days after it.

## 2. Measurement (`eng/verify/measure-rss.sh`)

1. Generate (or reuse) a loaded database.
2. Boot the app against it, wait for `/health`.
3. Sample RSS every 2s for the app process and the Postgres container: **at rest** (60s after
   warm-up) and **under the Playwright suite** (peak of the samples).
4. Print a table; write the same table into `06-deployment.md` §2 under a **Measured** heading,
   keeping the estimate table above it. Record row counts and the date.
5. Spot-check page timings (`/billing/dues`, `/admin/audit`, `/billing/reports`, `/dashboard`,
   `/pharmacy/stock`) with `curl -w %{time_total}` over the loaded DB — 5 samples, report median.

On the VM the same script runs with `docker stats --no-stream` as the sampler instead of `ps`.

## 3. Go-live rehearsal (`eng/verify/golive-rehearsal.py`)

Against a scratch database and a scratch app instance (never production):

1. Boot **with** seeding to create the demo cast, then stop.
2. Reboot with `Seed:DevUsers=false` — prove seeding is off (no new accounts appear; the run is
   the control for step 3's claim that rotation, not absence, is what killed the password).
3. Through the real `/admin/users` screens as `admin`: rotate `admin`'s own password, rotate one
   keeper account, deactivate the rest of the demo cast.
4. Prove: `Demo#1234` signs in **nowhere** (try 3 accounts); the rotated credential signs in; a
   deactivated account is refused; `/health` is 200.
5. Query `/admin/masters` for provisional prices and require zero (P8).

## 4. Docs

- `06-deployment.md` §2: measured table + date + row counts; §5's capacity paragraph gains a
  measured line (it currently reasons from first principles and says so).
- `notes.md`: row counts, both measurements, the abort-criterion verdict, and what the rehearsal
  proved.
- `docs/specs/README.md`: index row.

## 5. Verification

- Generator run on a scratch DB, then a **second** run to prove idempotency.
- `measure-rss.sh` on the Mac and on the VM.
- `golive-rehearsal.py` green.
- Full existing suite green afterwards (nothing here may change app behaviour): .NET tests,
  end-to-end scripts, Playwright, upgrade gate, CI greps.
