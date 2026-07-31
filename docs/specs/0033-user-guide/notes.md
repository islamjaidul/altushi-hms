# 0033 — Notes

## Acceptance criteria — how each was verified (2026-07-29)

1. **URL/label/username accuracy** — every route (49), sidebar label (52) and demo
   username (12) in `user-guide.md` was grepped against `src/Hms.Web/ModuleNav.cs`,
   `src/Hms.Web/Pages/**/*.cshtml` and `src/Hms.Web/DevSeed.cs`; zero misses.
2. **Money claims trace to code** — the AUTOMATIC/MANUAL statements come from a dedicated
   exploration of `BillingService.cs` (encounter/folio sweep), `IpdBilling.cs` (bed-day
   catch-up, admission fee, indents, settlement), `OtBilling.cs` (completion posts fees in
   the case's transaction), `DiagnosticsRelease.cs` (pay-before-sample), `EmrOrdering.cs`
   (doctor orders post unbilled lines), `PharmacySale.cs` (own PHARM encounter, no
   prescription link), `DayCloseService.cs` (summary rows; nothing writes ledger entries).
   The ৳200 tier-0 discount band re-verified at `DevSeed.cs:158`.
3. **Coverage** — 12 role sections (Part 4), all 14 built modules appear in workflows; the
   8 unbuilt modules are named in Part 1 and Part 5 "Known limits", Accounts explicitly.
4. **spec-auditor** — run at close on 2026-07-29. Verdict: **Compliant** — no High or
   Medium issues, no index drift, no boundary contradiction. One Low finding (this AC's
   own evidence trail was a forward reference), closed by this line.

## Deliberate choices

- The guide names four **known quirks** as operator-facing notes rather than hiding them:
  the pharmacist post-open redirect to a denied page, the carry-close approval having no
  screen, the IPD menu group rendering under a "Front Desk" heading, and the "AT LEAST"
  floor estimate on the Help Desk. These mirror open items in `docs/qa/module-coverage.md`.

## Follow-ups

- When any of the four quirks is fixed, update the matching guide line (the guide's footer
  states this obligation).
- If a real SMS gateway is procured, Part 5 "SMS is simulated" must change.
- At go-live, the demo-login table's warning becomes operative — consider replacing the
  table with the hospital's real role/account list in the deployed copy.
