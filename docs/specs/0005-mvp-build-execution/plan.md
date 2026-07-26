# 0005 — Plan

## Approved: 2026-07-26 (user approved via plan-mode review in-session; archived verbatim)

# Plan: Phase-by-Phase Development Plan per `docs/staff_engineer_prompt.md` (→ spec 0005)

## Context

The repo is docs-only, pre-implementation. The architecture package (spec 0003) and the staff-engineer handoff prompt (spec 0004) are complete. The staff-engineer prompt's **FIRST RESPONSE FORMAT** defines the next unit of work: before any code — (1) mission restatement + top implementation risks, (2) an S1 task breakdown that **becomes the S1 spec in `docs/specs/`**, (3) the solution/repo layout mapped to ADR-0003's modules, (4) architecture-vs-.NET conflicts flagged now. The user wants this expanded into a comprehensive phase-by-phase plan covering S1–S7 (`08-build-plan.md`).

**User decisions (asked & answered):** docs only — no S1 coding this session; one spec now (`0005`), later sprints get their own specs (0006+) when they start, per G1.

## Execution steps

1. Create **`docs/specs/0005-mvp-build-execution/spec.md`** — Status `Draft`, PRD ref §9A/§7/§8/§11/§12. Problem: architecture approved but no engineer-level execution plan; Requirements: the four first-response items, S1–S7 phase detail, spec mapping; AC: contradicts no ADR, adds no scope, cites only real files/§.
2. Create **`docs/specs/0005-mvp-build-execution/plan.md`** — the comprehensive phase-by-phase plan (content below). (Plan-mode approval = the approval that archives it, per spec-flow.)
3. Create **`docs/specs/0005-mvp-build-execution/tasks.md`** — S1 task checklist (T1–T12 below) + one line per later sprint pointing at its future spec.
4. Add 0005 row to `docs/specs/README.md` index.
5. Run **`spec-auditor`** agent to verify compliance; fix findings.
6. Final chat message = the staff-engineer first response (mission ≤300 words + 3 risks + layout + conflicts), citing the spec.

House style: `§` refs, `[M]/[S]/[C]` tags, cite ADRs by number, mark estimates as estimates.

## Plan content (what goes in 0005/plan.md)

### A. Mission + top 3 risks (≤300 words)
Build the 8-module MVP (PRD §9A.2) on one 2 vCPU/3 GB VM per ADRs; TDD is the working method (G5–G9); golden thread runnable from S2; every sprint ends demo-resettable.
- **R1 Bangla PDF shaping** (edge 9, gates ADR-0009/0014): no library asserted capable until the S1 spike proves it with printed samples; fallback = single pooled headless-Chromium worker, RAM charged in 06 §2.
- **R2 Silent thermal/label printing** per counter (Spike B): browser print profiles on real hardware; PDF preview (template 4 pane) is the sanctioned demo fallback (edge 2).
- **R3 Memory/latency budget**: app ≤ 800 MB limit, billing p95 ≤ 1 s, §9A.4 timed tests (≤60 s registration, ≤2 min diagnostic invoice, keyboard-only) — automated from S4, measured in S6; ADR-0001 GC levers + reversal triggers.

### B. Solution layout (ADR-0003 mapping)
```
hms-erp.sln
src/Hms.Web                     Razor host, SSE endpoints, hosted workers, composition root
src/Hms.Kernel                  approvals · audit · numbering · print/PDF · authZ · outbox/events · jobs · settings · entitlements · tax stub (ADR-0018)
src/Modules/<M>/Hms.<M> + Hms.<M>.Contracts
    Registration · Appointments · Billing · Diagnostics · Lis · Dashboard · Admin · Notifications
tests/Hms.<M>.Tests             fast domain unit (majority of pyramid, G8)
tests/Hms.Integration.Tests     Testcontainers PostgreSQL — money invariants (G6), concurrency harness (G7), table-driven state machines (G9)
tests/Hms.Architecture.Tests    boundary rules (G16, NetArchTest-class — capability verified in S1), endpoint-policy coverage (G10)
tests/Hms.E2e.Tests             Playwright golden thread + timed §9A.4 (join CI in S4)
tests/Hms.PrintGolden.Tests     per-layout/paper-size golden files (ADR-0009)
deploy/                         compose.yml (+ demo profile, -p demoA/demoB), Caddyfile
demo/                           seed generator, demo-reset.sh, offline checklist
eng/                            CI: additive-migration SQL gate (03 §12), no-external-hosts image check, lock-file + vuln scan (G15), token/colour lint + F-key registry collision check (05 §7)
```
Schemas per module: `reg appt bill diag lis adm notif kernel` (03). Contracts named in 04: `IPatientLookup`, `IChargePoster`, `IReceipts`, `IApprovalEngine`.

### C. S1 — walking skeleton + risk spikes (the detailed breakdown; becomes tasks.md)
Dependency-ordered; each task ships red-first tests:
- **T1** Solution scaffold + CI skeleton (build/test/image; lock files; vuln scan; no-external-hosts check).
- **T2** Compose stack boots VM + Mac (caddy/app/db/backup, healthchecks, mem limits per 06 §2); verify SSE flush through Caddy here.
- **T3** Architecture-test project — plant a deliberate boundary violation, watch it fail, remove it (proves G16 before any module code).
- **T4** Kernel settings + signed entitlement file load (ADR-0016; test: tampered signature rejected, module gate at 3 choke points).
- **T5** AuthN/authZ: Identity, cookie sessions, idle-lock, `module.action` policies + nav composition (ADR-0019; tests: anonymous surface = login+health only; nav = permissions ∩ entitlements; lockout audited).
- **T6** Audit writer (ADR-0011; test: audit row in same tx, rollback removes both; no UPDATE/DELETE grants).
- **T7** Number series (ADR-0004; **first G7 test**: parallel issuance on real Postgres — gap-free, collision-free, fiscal-year reset from config P1).
- **T8** Outbox + jobs (`FOR UPDATE SKIP LOCKED` drain; idempotent handler retry; poison-park test).
- **T9** UI shell: tokens (05 §1), sidebar/topbar/status-footer shell (05 §2), 4 templates skeleton (05 §4), F-key registry as declared data (collision fails CI), type-ahead + barcode-wedge kernel JS contracts.
- **T10** **Spike A — Bangla PDF shaping** (gates ADR-0009/0014): candidate engines vs Bengali conjuncts/matras, embedded Noto Bengali, printed sample = pass artifact; on fail invoke pooled-Chromium fallback + ADR update *before* dependent work. Golden-file harness born here.
- **T11** **Spike B — silent thermal + label print** on real hardware (58/80 mm + label; print-profile runbook; feeds P12 hardware baseline).
- **T12** Sprint close: `demo-reset` stub + S1 exit — two roles log in on the laptop, role-filtered nav, Bangla-footered test PDF prints.

### D. S2–S7 (per sprint: scope · key tests · exit · edge cases owned · spec id)
Standing rules every sprint: red money test blocks merge; sprint ends demo-reset + thread run; each §-cited behaviour lands with its edge cases; PRs ≤ ~400 lines net naming spec + tests.
- **S2 (spec 0006) — money spine:** registration (dup-warning, age⇄DOB, unknown-emergency, no-phone → edges 23–26), directory/search (trigram ≤ 300 ms), UHID + card print; encounter + charge lines + OPD invoice (POS template) + receipts/tenders + due (row-lock); counter sessions; thermal/A4/PDF prints. G6 invariants become executable now (`net = gross − discount + tax + rounding_adj`; `Σ receipts + due = net`; rounding rule 03 §6 half-up-once). Exit: half-thread; ≤ 60 s registration by a non-developer.
- **S3 (spec 0007) — diagnostics + approval engine:** catalog + effective-dated rate versions (exclusion constraint; edge 13 byte-identical historical invoice test), diagnostic order invoice (TAT promise, referrer), **approval engine** (thresholds/delegation/escalation as data, P4; inbox SSE), unbilled-charge SSE seam, label printing on payment (edge 27 reprint-same-barcode). Exit: discount-above-threshold demo beat end-to-end.
- **S4 (spec 0008) — LIS + day-close:** sample M:N model (edge 33), rejection→child chain, pipeline board scan-advance, result entry + ref ranges/flags (age-precision, edge 26), verify/e-sign incl. reporting consultant (edge 34), amendment versions (edge 22), delivery log; report-ready notifications + simulation tray (edge 3); **day-close** (variance recorded-not-blocked 18, carry-close 17, reopen⚿, summary holding rows), refund-after-close (edge 20). Exit: full golden thread; §9A.4 timed tests in CI; concurrency harness green (G7: parallel serials/dues/numbering/day-close-vs-late-receipt).
- **S5 (spec 0009) — dashboard, admin, import, hardening:** MD dashboard read-models + drill-downs (03 §10), masters, **import pipeline** (ADR-0010: mapping templates, error round-trip, reversal batch; edges 11/12), provisional flags + go-live checklist, audit viewer, 2FA enrolment (approver roles), backup/restore scripts + drill (ADR-0013, ≤ 5 min rehearsed), disk/clock sentinels (edges 31/32). Exit: F1 construction-kit beat.
- **S6 (spec 0010) — demo kit + performance:** seed generator (07 §1: 90-day history, §14 shape, seed_tag) + golden snapshot + `demo-reset.sh` < 30 s + dual instances; demo-load test 25–40 operators → **measure the 06 §2 budget table (replaces estimates — DoD)**; micro-help for 16 screens; print golden-file suite complete; power-cut drill (edge 7); accessibility/keyboard pass. Exit: offline checklist (07 §3) passes on a cold laptop.
- **S7 (spec 0011) — buffer + rehearsals:** fix-list; two 20-min runbook rehearsals with a non-team driver (edge 10); runbook set (06 §7); RC tagged. Release gate = the 9-item MVP DoD in `architect_prompt.md`.
Cut-list order (PM decides, 08 §4) restated; never-cut: money integrity, approvals, day-close, offline posture, seeded history, reset.

### E. Conflicts / verify-in-S1 flags (G4 — nothing asserted unverified)
1. `adm.rate_version` GiST exclusion constraint + `reg.patient` generated dmetaphone column aren't expressible in EF Core's model API → hand-written SQL in migrations (sanctioned by 00 §2); dmetaphone (fuzzystrmatch) immutability for a generated column **verified in S1, not assumed**.
2. SSE through Caddy needs flush/no-buffer config — T2 proves it before S3 depends on it.
3. Testcontainers needs Docker on CI runners — CI infra requirement stated up front.
4. NetArchTest (or equivalent) capability for the exact G16 assertions verified in T3 before being claimed.
5. "Current LTS at build start" (ADR-0001) pinned explicitly in the S1 spec when tooling is installed.

## Verification
- `spec-auditor` agent passes (index row, lifecycle, plan archived, no drift).
- Grep-check: every §/ADR/edge citation in the new docs resolves to a real target.
- No ADR contradiction; no scope beyond §9A.2; estimates marked as estimates.
