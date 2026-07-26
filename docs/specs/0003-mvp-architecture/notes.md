# 0003 — Notes

- **Plan approval mechanics:** the plan was iterated three times in-session (initial → .NET/extensibility steer → Altushi HMS design reference) and a copy was also sent to a cloud Ultraplan session for refinement; the user proceeded locally by exiting plan mode, which this spec treats as approval of the archived `plan.md`. If the cloud-refined plan returns with material changes, supersede via a new spec.
- **Deviation from the brief's gate:** `docs/architect_prompt.md` says "stop after Deliverable 2 for PM review before writing code". The user (acting PM) directed all 10 *documents* in one pass; no code was written, so the gate's intent (no implementation before ADR approval) is preserved — all deliverables are marked **Draft for PM review**.
- **Estimates outstanding (DoD carry-over):** the `06-deployment.md` memory-budget table and capacity ceiling are reasoned estimates; the DoD item "validated against actual measured container usage" is deferred to build-plan **S6** (demo-load test). The Bangla-PDF shaping spike (ADR-0009/0014) and silent-print spike are **S1** gates.
- **Design reference:** the 3.5 MB `Altushi HMS (standalone).html` was decoded (bundler manifest → Vue-style app + fonts) to extract nav structure, 9 roles, ~49 page ids, tokens (Public Sans, `#1B5E9C` primary, dept accent colours) and patterns; archived verbatim at `docs/architecture/assets/altushi-hms-demo.html`. It depicts the *full* ERP — MVP builds its 8-module subset; the rest validates the C2 seams.
- **Acceptance criteria — how verified at close:**
  1. Files 00–09 + 19 ADRs exist (`ls` sweep); Q1–Q15 each map to one ADR (`01-adr/README.md` coverage checklist, all ticked).
  2. Memory budget table (`06-deployment.md` §2) totals ~1.9 GB limits vs 2.6 GB allowance, ≥ ~700 MB headroom, swap declared crash-cushion-only; figures flagged as estimates.
  3. Edge-case coverage table in `tasks.md` maps all 34 brief items to their discharging doc/ADR.
  4. Role boundary + index integrity checked by the `spec-auditor` agent run recorded in the audit record below.
  5. `§` citations swept by grep against the PRD's section headers; two self-reference typos fixed during the sweep.
- **Follow-ups:**
  - PM answers to `09-questions-for-pm.md` P1–P12 (P11 — demo date/team — gates the build plan).
  - PM review of the draft deliverables → flip statuses from Draft to Approved (new spec not required for status flips; material changes supersede).
  - Implementation itself is **not** covered by this spec — the first build sprint needs its own spec(s).

## Audit record — spec-auditor run, 2026-07-26 (post-close)

**Clean:** spec archive integrity (IDs sequential, 0000–0002 untouched, index matches disk); no unspecified changes (every touched file maps to this spec); ADR coverage Q1–Q15 complete, 1:1, no duplicates; MVP scope matches §9A.2 exactly; role boundary intact both directions (business calls routed to `09-questions-for-pm.md`, PRD untouched); all § citations resolve; no `[obs:]` tags added outside the PRD.

**Findings (all fixed same day, this change set):**
1. *(Medium)* `00-architecture-overview.md` budget summary disagreed with `06-deployment.md` §2 by ~100 MB → now quotes 06 verbatim and names it the single source of truth.
2. *(Medium)* All ADRs `Accepted` while deliverables are Draft-for-PM-review → clarifying definition added to `01-adr/README.md` ("Accepted = architect-final; PM answers supersede via new ADR").
3. *(Low)* This notes file claimed a recorded audit that wasn't yet recorded → this section is the record.
4. *(Low)* `Done` readability → convention line added to `docs/specs/README.md` (Done = work produced, not externally approved).
5. *(Low)* Six ADRs had qualifiers inside the Status enum → moved to a `Note:` line (`0008, 0009, 0012, 0014, 0017, 0018`).

**Not verifiable by audit:** memory-budget numbers themselves (estimates pending S6 measurement — labelled as such).
