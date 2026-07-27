# 0021 — Tasks

- [x] Advanced edge-case probe across the lifecycle (8 scenarios, 7 roles)
- [x] Spec + plan archived; index row; P21 appended to PM questions
- [x] Gap 1+2: `IpdBilling.CanSettle` admits Death/Absconded; `ConfirmSettlementAsync`
      promotes to `financially_settled` **only** from `clinically_cleared`; discharge screen
      gains a "close the bill" branch that never says "discharge"
- [x] Gap 3: `bill.invoice.submission_token` + unique partial index (`SubmissionToken`
      migration); `FindBySubmissionAsync`; both invoice creators; all four issuing screens
      mint/carry/consume a token; `Guid.Empty` normalised to NULL so tokenless posts are
      unaffected; client-side latch on the clicked button as defence in depth
- [x] Gap 4 (found while fixing): a folio settled while the patient was R4-blocked left the
      admission permanently undischargeable — discharge now recognises a locked, invoiced folio
- [x] 5 integration tests (repeat, concurrent, tokenless, folio settlement, blocked-then-released)
- [x] lifecycle-thread steps 10–11; `spec-0021.spec.ts` (4 UI tests)
- [x] Test isolation: `NumberSeriesTests` no longer borrows the real `invoice` series
- [x] `eng/verify/edge-cases.py` — the probe promoted to a permanent 8-case suite (death,
      abscond, double-submit, over-issue, R4 freeze, settled-while-blocked, over-discount,
      public-surface leak), wired into the upgrade gate and repeat-run safe
- [x] Message fix the suite caught: a blocked patient was told they were not "clinically
      cleared" instead of being told about the block
- [x] Full verification: 116 .NET · 7 end-to-end scripts · 181 UI tests · upgrade gate · 4 CI gates
