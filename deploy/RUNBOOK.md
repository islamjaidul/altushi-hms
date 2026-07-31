# HMS Runbook (06 §7 index — grows with the build)

## 1. Install (VM)
Debian-stable host: Docker + chrony + unattended-upgrades + SSH (outbound tunnel only, ADR-0005).
`cd deploy && HMS_DB_SUPER_PASSWORD=… HMS_DB_APP_PASSWORD=… HMS_DB_MIGRATOR_PASSWORD=… docker compose up -d`.
Migrations run at app start under an advisory lock; healthchecks gate startup order.

## 2. Laptop demo
`docker compose --profile demo up -d` (plain HTTP, SMS simulation). Two instances (edge 6):
`docker compose -p demoA up -d` / `-p demoB` with distinct `HMS_HTTP_PORT`.

## 3. Counter PC setup
Install the Caddy internal CA cert (script in S6 polish); set the browser print profile
(silent print, paper sizes 58/80 mm + A4) per printer model; scanner in keyboard-wedge mode,
suffix Enter. Verification page: /health then a test label (Spike B sign-off, spec 0005 notes).

## 4. Update & rollback
`docker compose pull && docker compose up -d` off-peak (sub-minute swap, §8 N6).
Rollback = pin previous image tag; schema is additive-only (03 §12) so old app runs on new schema.

## 5. Backup & restore
Nightly dump + sha256 (backup container, 7-day retention). Restore: `deploy/restore.sh latest`
or into a scratch db for the weekly drill: `deploy/restore.sh <dump> hms_drill` (≤ 5 min target).
PITR: stop app → point recovery at WAL archive volume → `recovery_target_time` → verify → promote.

## 6. Power-cut recovery check
Boot the VM; compose `restart: unless-stopped` brings the stack up (target ≤ 2 min). Verify:
/health 200 → open sessions intact → last invoice number matches the audit trail (no duplicates
by construction, ADR-0004).

## 7. Demo reset & dual instance
`demo/make-snapshot.sh` after seeding → `demo/demo-reset.sh` before every run (< 30 s, self-timed).

## 8. Disk pressure
Alert at 70% (admin banner), 85% pauses PDF archival + prunes oldest local backups (off-site
copies exist). The DB is never the first casualty (06 §3). Sentinel job lands with S6 polish.

## 9. Go-live switch (execute BEFORE the first real patient record — spec 0015, review §4.4)
Run in order, on the PM's written instruction (P16 names the owner):
1. **Seed off:** set `HMS_SEED=false` in `deploy/.env`, redeploy (§4). Seeding is idempotent
   and only ever ran with the flag on — no schema change involved.
2. **Rotate the demo cast:** in `/admin/users`, use **Set password** on every account that stays
   (real staff take over their own accounts) and **Deactivate** every demo account that doesn't
   (deactivation, never deletion — receipts keep their names). Both actions bump the security
   stamp, so any live demo session dies immediately rather than at its next sign-in.
3. **Verify:** the shared demo password signs in **nowhere** (try 2–3 accounts); `/health` returns
   `{"status":"ok"}`; an operator can still sign in with a rotated credential.
4. **Provisional prices:** `/admin/masters` shows zero provisional items (P8 — billing on a
   provisional price is blocked from go-live). A genuinely free item is priced **0**, not left
   unpriced; unpriced is what the pill counts.
5. **Snapshot:** take a labelled backup (`§5`) marked `pre-go-live` — the restore point that
   contains no real patient data.

**Rehearse first, always.** `eng/verify/golive-rehearsal.sh` runs all of this against two scratch
databases and proves each claim (spec 0023) — including that the seed flag really gates seeding.
Run it before the live cutover; it takes about two minutes and touches nothing real.

## 10. The standalone HRM SKU (ADR-0025)

A second product from the same source tree: `Hms.Hr.Web` instead of `Hms.Web`, three schemas
(`kernel`, `adm`, `hr`) instead of fourteen, no clinical code in the image.

**A real HRM-only customer** gets `compose.hrm.yml` unmodified — its own Postgres, its own backup
loop, its own Caddy, on its own VM. Nothing below applies to them.

**The shared demo box** (103.132.96.250) cannot afford a second Postgres: four products share
~3 GB and it is already into swap. `compose.hrm.vm.yml` therefore runs the app alone, against the
ERP's Postgres server in a separate `hrm` database, behind the host-level Caddy on `:8091`:

```
git -C /opt/altushi-hms pull --ff-only
docker compose -f /opt/altushi-hms/deploy/compose.hrm.yml \
               -f /opt/altushi-hms/deploy/compose.hrm.vm.yml build app
docker compose -f … -f … up -d app          # `app` only — see the overlay's comment
```

One-time, before the first boot — the database and its extensions (roles are cluster-wide and
already exist from the ERP):

```sql
CREATE DATABASE hrm;
GRANT ALL ON DATABASE hrm TO hms_migrator;
GRANT CONNECT ON DATABASE hrm TO hms_app;
\c hrm
CREATE EXTENSION IF NOT EXISTS btree_gist;   -- effective-dated exclusion constraints
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS fuzzystrmatch;
```

**Known gap on the demo box: `hms-backup-1` dumps only the `hms` database.** The `hrm` database is
unbacked-up until the backup loop learns a second `PGDATABASE`, or the HRM moves to its own stack.
Do not put anything on this deployment you would mind losing.

### The host-kind interlock

`kernel.host_kind` records which product created a database. The ERP host adopts an HRM database
and migrates the remaining schemas (that is the upgrade path a customer buys). The HRM host
**refuses** an ERP database rather than half-serving it. If a host exits at startup complaining
about host kind, it is pointed at the wrong database — that is the guard working.

### Entitlements and vendor key custody

Each SKU boots with a signed entitlement naming the customer, the modules and an expiry
(ADR-0016); the image bakes only the **public** key. `deploy/entitlements/*.json` are **dev**
licences signed with `eng/dev-keys/` — gitignored, dev-only, and named `(DEV)` in the payload so
they cannot be mistaken for a sale.

**The vendor private key never goes on a customer machine, or on this VM.** Signing happens on the
vendor's own machine; what travels is the resulting `.json`. Rotation means re-signing every live
customer's file and shipping a new image with the new public key — so treat the key as the most
sensitive artifact in the product and keep an offline copy.

A licence past expiry does not stop the product: 30 days of grace with a banner, then read-only —
GETs still work, mutations refuse (P6). Customers get their data back regardless of the commercial
relationship.
