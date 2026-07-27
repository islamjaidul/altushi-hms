# 0023 — Tasks

- [x] `HistoryGenerator` writing §14-shaped days through the real services on a backdated clock
- [x] `generate-history` command wired into `Program.cs` (command, never a config flag)
- [x] Idempotency marker in `kernel.setting`; a re-run resumes instead of duplicating
- [x] Beds topped up to the §14 figure of 100; ward state inherited on a resumed run
- [x] Replenishment on the reorder rule so the pharmacy thread does not starve after day three
- [x] `eng/verify/measure-rss.sh` — at-rest and under-suite RSS, page timings, abort-criterion verdict
- [x] `eng/verify/golive-rehearsal.sh` + `.py` — RUNBOOK §9 executed end to end on scratch databases
- [x] **Defect:** `/admin/users` had no password reset; RUNBOOK §9 step 2 asked for an action the
      product could not perform. Added `OnPostResetPassword` (stamp bump + tier-2 audit) and the
      row control.
- [x] **Defect:** the percentage-marker service was seeded with no rate version, so the go-live
      gate "zero provisional prices" could never be cleared. An item priced zero is priced.
- [x] RUNBOOK §9 updated: the new control, the `{"status":"ok"}` health shape, the rehearsal step
- [x] `06-deployment.md` §2 gains the measured table; §5 gains the measured capacity line
- [x] Verification: generator run + repeat run, rehearsal green, measurement recorded,
      full suite (unit/integration/architecture, end-to-end scripts, Playwright, upgrade gate)
