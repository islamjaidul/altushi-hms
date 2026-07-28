# 0028 — Tasks

## The document

- [x] `docs/qa/patient-lifecycle.md` — 169 cases across 17 stages, every one naming its
      performing role, screen and coverage marker
- [x] Gap register with severities — 39 gaps, 13 High
- [x] `docs/qa/README.md` — tiers, running, deployments, what a production run leaves behind

## Harness and runner

- [x] `eng/verify/_harness.py` — one `Session`, `BASE_URL` awareness, `guard()` interlock,
      `case()`/`check()` reporting, role tracking, run manifest, `tag()` for shared targets
- [x] `eng/verify/role-journeys.py` — LC-ROLE, 12 roles × 64 protected routes, both directions
- [x] `eng/verify/lifecycle-suite.py` — tier runner, role-completeness interlock, JSON summary
- [x] `eng/check-lifecycle-traceability.sh` — four joins between document and source
- [x] Wire the guard into `.github/workflows/ci.yml`
- [ ] Retro-fit the thirteen existing scripts onto `_harness.py` — deferred, see notes

## Agent

- [x] `.claude/agents/qa-lifecycle.md`
- [x] `.claude/skills/qa-lifecycle/SKILL.md`

## Verification

- [x] `role-journeys.py` green — 15 cases, 12/12 roles, 0 failures
- [x] All 9 t1 threads pass on first run; 3 fail on a second pass — fixture exhaustion, F3
- [x] `golden-thread.py` fails only on its absolute-total assertion, confirming the t2 boundary
- [x] Traceability guard green — 169 cases, 130 covered, 39 gaps
- [x] Guard proven to bite: it caught `appointments.create` enforced nowhere, and the
      `/emr/charts` optional-route-parameter mismatch
- [x] t0 writes nothing — row counts unchanged before and after
- [x] Non-localhost mutating run refused without `HMS_QA_ENV` + `HMS_QA_CONFIRM`
- [x] t2 against `prod` refused unconditionally
- [x] t0 against the production deployment (read-only) — surfaced the grant drift in F1
- [ ] t1 against a remote target — blocked until the legacy threads honour BASE_URL

## Follow-ups

- [ ] Remediation spec for the 13 High-severity gaps
- [ ] `appointments.create` enforcement (LC-QUE-08)
- [ ] Eleven protected routes absent from the UI suite (LC-XCUT-13)
