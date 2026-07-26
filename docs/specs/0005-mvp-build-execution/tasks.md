# 0005 — Tasks

## Plan production (this session)

- [x] `spec.md` written (Status Approved)
- [x] Approved plan archived verbatim to `plan.md`
- [x] Index row added to `docs/specs/README.md`
- [x] `spec-auditor` run — compliant, 0 High/Medium; 2 Low hygiene notes fixed (approval-context line, LTS-pin routing to notes.md)
- [x] First-response summary delivered (mission + risks + layout + conflicts)

## S1 implementation (gated on this spec's approval; dependency order — see plan.md §C for test-first notes)

- [x] T1 Solution scaffold + CI skeleton (lock files, vuln scan, no-external-hosts check) — record the pinned .NET LTS version in `notes.md`, not by editing `plan.md`
- [x] T2 Compose stack boots on VM + Mac (healthchecks, 06 §2 mem limits; SSE-through-Caddy verified)
- [x] T3 Architecture-test project red on planted boundary violation, then green (G16)
- [x] T4 Kernel settings + signed entitlement load (ADR-0016)
- [x] T5 AuthN/session/idle-lock + `module.action` authZ + nav composition (ADR-0019)
- [x] T6 Audit writer, same-transaction (ADR-0011)
- [x] T7 Number series + parallel-issuance concurrency test on real Postgres (ADR-0004, first G7 test)
- [x] T8 Outbox + jobs drain (`FOR UPDATE SKIP LOCKED`, idempotent retry, poison-park)
- [x] T9 UI shell: tokens, shell, 4 templates, F-key registry (collision fails CI), type-ahead + barcode-wedge JS
- [x] T10 Spike A — Bangla PDF shaping (gates ADR-0009/0014; printed sample = pass artifact; fallback invoked on fail)
- [ ] T11 Spike B — silent thermal + label print on real hardware (print-profile runbook; feeds P12)
- [x] T12 S1 exit verification: two roles log in on laptop, role-filtered nav, Bangla-footered test PDF prints

## Later sprints (each opens its own spec at sprint start, per G1)

- [x] S2 money spine → spec 0006 (Done)
- [x] S3 diagnostics + approval engine → spec 0007 (Done)
- [x] S4 LIS + day-close → spec 0008 (Done)
- [x] S5 dashboard/admin/import/hardening → spec 0009 (Done)
- [ ] S6 demo kit + performance measurement → spec 0010 (In Progress — measurement + history generator pending hardware)
- [ ] S7 buffer + rehearsals, RC → spec 0011 (Draft — gated on S6 close + UI pass)
