# 06 — Deployment

- **Status:** Draft for PM review · **Date:** 2026-07-26 · **Spec:** `docs/specs/0003-mvp-architecture/`
- Target: **one VM, 2 vCPU / 3 GB RAM**, Debian-stable-class Linux, Docker Compose. Identical compose file (different profile) runs the presenter's laptop demo and the developer's Mac (Docker Desktop, multi-arch images — ADR-0001).

## 1. Topology

```
docker compose (project: hms)
├── caddy      reverse proxy · LAN TLS (internal CA) · static assets · gzip
├── app        ASP.NET Core (modules + kernel + hosted workers)   [the only app process]
├── db         PostgreSQL (data, jobs, audit)
├── backup     alpine+postgres-client cron: WAL archive mgmt, nightly base backup,
│              off-site push when online, restore drill target     [transient duty cycle]
└── volumes    pgdata · wal_archive · backups · docs (PDF archive) · caddy_data
```

No Kubernetes, no swarm — `restart: unless-stopped` + healthchecks are the supervision story (§16.1 A4: one IT-capable person). Host gets: Docker, chrony (NTP), unattended security updates, SSH (outbound-tunnel support access only — ADR-0005).

## 2. Memory budget table (the brief's required deliverable)

**All figures are estimates until measured against real containers** (spec 0003 notes; DoD requires validation before the demo). Limits are enforced via compose `mem_limit`.

| Component | Steady-state est. | Hard limit | Notes |
|---|---|---|---|
| OS + systemd + sshd + chrony + Docker daemon | ~350 MB | (host) | Debian minimal; no GUI |
| PostgreSQL | ~300–450 MB | **550 MB** | `shared_buffers=256MB`, `work_mem=8MB`, `max_connections=40` (app pools ≤ 20) |
| ASP.NET Core app | ~250–400 MB | **800 MB** | Server GC off (workstation GC), `DOTNET_GCHeapHardLimit` aligned to limit; includes workers |
| Caddy | ~30–50 MB | **64 MB** | static + TLS only |
| Backup container | ~20 MB idle | **128 MB** | pg_dump spikes bounded; runs off-peak |
| **Total limits** | | **1,542 MB + ~350 host = ~1.9 GB** | |
| **Headroom vs 2.6 GB allowance** | | **≥ ~700 MB** | above the mandated 400 MB floor; page cache uses the slack |

Swap: 1 GB configured with `swappiness=10` as a crash cushion only — **steady state must not touch it** (alert if swap-in > 0 sustained; violates the brief otherwise).

## 3. Container & operational details

- **Images:** distroless/chiseled .NET runtime image for `app` (smaller RSS + attack surface); pinned digests; multi-arch (amd64 VM / arm64 Mac).
- **Healthchecks:** `app` deep-checks DB connectivity + migration state; `caddy` checks `app`; compose `depends_on: condition: service_healthy` gives clean cold-boot ordering after a power cut (edge 7 — target full service ≤ 2 min; measure in the drill).
- **Logs:** json-file driver, `max-size=10m, max-file=5` per container (bounded by construction); app logs are structured, correlation-id'd (ADR-0011), shipped nowhere in MVP (single box — `docker logs` + a log-view admin page suffice).
- **Disk (edge 32):** minimum **60 GB SSD** (estimate: OS 10 + images 5 + data 2/yr + PDFs 10–20/yr + WAL/backups 15 + slack). A disk-watch job alerts at 70% (banner to admin role + vendor via next online window) and at 85% pauses PDF archival + oldest local-backup pruning (off-site copies already exist) — the DB is never the first casualty.
- **Clock (edge 31):** chrony with LAN-tolerant config; if NTP is unreachable (offline site), the app monitors DB-vs-app clock skew and **stamps all money/audit events from the DB clock** (single source of truth on one box); admin banner when wall-clock drift is detected against known-good boot time. Time is financial evidence: manual host clock changes are logged by a boot/cron sentinel into the audit trail.
- **TLS:** Caddy internal CA; setup script installs the CA cert on counter PCs (documented runbook step, ADR-0019).
- **Updates:** `compose pull && compose up -d` off-peak; image-tag rollback; migrations additive-only (03 §12) so rollback of the app never strands the schema. §8 N6: registration/billing endpoints stay up during routine maintenance because updates are a sub-minute container swap, drilled in the runbook.

## 4. Laptop demo variant (edges 1, 5, 6)

`compose --profile demo`: same images + seeded DB volume; Caddy serves plain HTTP on localhost (no CA step); SMS forced to simulation; `demo-reset.sh` (see `07-demo-kit.md`) restores the golden snapshot in target **≤ 30 s**. **Two isolated instances** (edge 6): `compose -p demoA/-p demoB` with distinct ports/volumes — the script wraps this. Runs offline by construction: every asset is in the images.

## 5. Honest capacity & the scale-up path (the brief's §3 requirement)

**Estimated ceiling of the 3 GB box: ~25–40 concurrent operators** on the golden-thread mix (registration + billing + LIS + dashboard) at ≤ 1 s perceived billing latency (§8 N1). Reasoning from first principles (to be validated by the demo-load test, not asserted as measured): the workload is small-row OLTP — a billing save is ~10–20 statements in one transaction; 40 operators ≈ single-digit requests/sec sustained, well inside 2 vCPU — **RAM for DB cache, not CPU, is the binding constraint**, which is why the budget gives Postgres + page cache the slack. The §14 design ceiling (150 operators, 1,200 diagnostic invoices/day) **does not fit this box and we do not claim it does.**

**Scale-up triggers (measured, alerting in-app admin page):**

| Metric | Threshold | First action |
|---|---|---|
| Billing endpoint p95 | > 700 ms sustained 15 min | Grow VM RAM → raise `shared_buffers` + app limit (same box class → 8 GB) |
| DB cache hit ratio | < 98% sustained | same as above (RAM first) |
| CPU steal/util | > 70% sustained peak-hour | 2 → 4 vCPU |
| Swap-in | > 0 steady state | immediate RAM growth (budget breach) |
| Connection pool waits | > 5/min | raise pool ceiling with RAM; then separate DB host |

**Path to opening-day load (30–100 operators) with zero re-architecture:** ① bigger single VM (8 GB / 4 vCPU — covers ~100 operators, estimate) → ② move `db` to its own host (compose file split; connection string change; correctness unaffected because all concurrency control is in the DB — ADR-0015) → ③ multiple `app` replicas behind Caddy (stateless by design: cookie ticket + DB state; SSE reconnects). Each step is an operation, not a code change. Beyond that (§14 ceiling, 150 ops): read replicas for dashboards/reports — already isolated behind read-model views (03 §10).

## 6. Backup/restore operations

Per ADR-0013: WAL archiving (RPO ≈ ≤ 5 min target), nightly base backup + manifest + audit hash-chain heads, off-site push when online, 7-day on-box retention, weekly automated **restore drill** into a scratch container (CI on the vendor side; on-site script for the demo's "show me a restore" — edge 8). Restore modes: PITR-to-timestamp and bare-metal re-install. Customer full export endpoint per N3.

## 7. Runbook index (to be written with the build, listed here as deliverable contents)

Install (VM + laptop) · counter-PC setup (CA cert, printer profiles, scanner test) · update & rollback · power-cut recovery check · backup verification & restore drill · disk-pressure response · support tunnel · demo reset & dual-instance.
