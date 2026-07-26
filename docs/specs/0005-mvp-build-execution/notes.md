# 0005 — Notes

- 2026-07-26 — **Spike A (Bangla PDF shaping) PASSED on screen.** Engine: QuestPDF 2026.7.1 (Skia/HarfBuzz shaping) with self-hosted Noto Sans Bengali (OFL). Artifact: `eng/spike-artifacts/bangla-sample.pdf` — conjuncts (স্বা, ক্ষ, ত্র), reph (র্ট) and matras verified visually correct; PDF text-extraction garbling is the expected glyph-mapping artifact, not a render defect. **Printed-sample sign-off still pending physical printer access** (same constraint as Spike B). Licence: QuestPDF Community (free below revenue threshold) — flagged for the vendor's commercial decision in the ADR-0009 amendment.

- 2026-07-26 — **.NET LTS pinned: SDK 10.0.302** (ADR-0001 "current LTS at build start"; recorded here rather than editing plan.md, per spec-auditor guidance). Installed user-local (`~/.dotnet`) because the Homebrew cask needs interactive sudo unavailable in this environment.
- 2026-07-26 — **T3 boundary-test verification finding:** the first planted violation used a `const string` from another module — the C# compiler inlines consts, so no assembly reference lands in metadata and reflection-based (or NetArchTest-class) checks cannot see it. The planted violation had to use `typeof(...)` to register. Consequence recorded as a review rule: cross-module `const` access is invisible to the architecture gate; contracts must not expose consts whose values other modules might bake in. NetArchTest.Rules was added but the shipped tests use plain reflection over referenced-assembly metadata (fewer moving parts, same guarantee); the package will be dropped if still unused by S3.
- 2026-07-26 — **Spike B (silent thermal/label print on real hardware) cannot run in this environment** — no physical printers attached. The software side (print-view CSS `@page` sizes, browser print pipeline, print-profile runbook draft) is built; the on-hardware verification step is recorded as an open deviation to be run at the office/demo site before S1 is declared Done. PDF preview fallback (edge 2) is the sanctioned interim path.
- 2026-07-26 — **Full-phase development pass executed in one session** (S1 → S7 records).
  Domain cores, migrations, kernel engines and 80 automated tests (22 unit, 17 architecture,
  40 integration on real Postgres, 1 print-golden) are green; CI workflow + 4 guard gates in
  place. **Consolidated open items:** (1) the cross-sprint **UI pass** — the 16 screens of 05 §5
  on the S1 shell/templates (services + tests they call are done); (2) Spike B + Spike A printed
  sign-off on real printers; (3) S6 seed-history generator + measured memory table on target
  hardware; (4) S7 rehearsals/RC (human-gated). Each is recorded in its sprint spec's notes.
- 2026-07-26 — **Compose runtime brought live end-to-end** (T2 completion): db-init roles →
  migrations under advisory lock → seed → healthcheck → Caddy. Two dev-stage tradeoffs recorded:
  (1) app image is standard `aspnet:10.0` (not chiseled) because the compose healthcheck needs a
  shell — swap back to chiseled once the probe is a compiled binary; (2) the app connects as
  `hms_migrator` (object owner) for now — moving runtime traffic to least-privilege `hms_app`
  needs a cross-schema grant migration (kernel/reg/diag/lis/adm + default privileges). The C5
  no-DELETE guarantee itself is grant-tested in the integration suite either way.
