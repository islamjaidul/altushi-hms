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
