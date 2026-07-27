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
2. **Rotate the demo cast:** in `/admin/users`, set a strong unique password per account that
   stays (real staff take over their own accounts), **deactivate** every demo account that
   doesn't (deactivation, never deletion — receipts keep their names). The security-stamp
   bump kills any live demo session immediately.
3. **Verify:** the shared demo password signs in **nowhere** (try 2–3 accounts); `/health` 200;
   an operator can still sign in with a rotated credential.
4. **Provisional prices:** `/admin/masters` shows zero provisional items (P8 — billing on a
   provisional price is blocked from go-live).
5. **Snapshot:** take a labelled backup (`§5`) marked `pre-go-live` — the restore point that
   contains no real patient data.
