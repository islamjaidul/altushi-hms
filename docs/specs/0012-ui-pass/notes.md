# Notes — 0012 UI pass

## What the gap actually was

The services from S2–S5 were complete and test-proven; `src/Hms.Web/Pages/` contained Login,
Logout and Index. All fourteen `ModuleNav` routes 404'd — hence "no single menu is working".
Two further holes were found while wiring:

- **Appointments and Notifications were never composed.** Both `DbContext`s existed but were
  absent from `Program.cs` and had no migrations, so those two of the eight MVP modules could
  not run at all. `InitAppt` / `InitNotif` close that.
- **No counter-session screen.** `CollectAsync` requires an open session, so billing was
  unusable on a fresh install regardless of the POS. `/billing/session` is new (not in the
  original registry) and is the money path's real entry point.

## Decisions taken during implementation

**Scope.** The reference carries ~50 nav items across 13 groups; §9A.3 excludes pharmacy, IPD,
OT, stores, blood bank, HR and accounts from the MVP. The reference's *grammar* was implemented
across the 14 MVP routes; no excluded module gained a screen, route or nav entry. Four routes
were added inside existing MVP modules — `/billing/session`, `/diagnostics/delivery`,
`/lis/results`, `/admin/masters` — each serving a §9A.2 capability that had no screen.

**Icons without a CDN.** The reference uses Material Symbols. A 64-glyph subset (8 KB) is
vendored to `wwwroot/fonts`; `check-no-external-hosts.sh` stays green and the app renders
identically with the network unplugged (edge 1).

**The POS cart lives in the form, not the database.** Adding a line posts back and re-renders;
nothing is written until save. An abandoned bill therefore leaves no charge rows behind, and
there is no pre-invoice delete path to reconcile with "no financial hard deletes" (hard rule 4).
Prices are always re-resolved server-side from the rate plan — a posted price is ignored.

**Discount above threshold.** The counter cannot complete the bill: the request is raised, the
operator is told where it went, and the cart survives. When the supervisor approves, the POS
detects the unspent approval on next load and shows it. Consuming it is recorded by
`invoice.discount_approval_id`, so one approval cannot be spent twice.

**Lab stage is derived, not stored.** The board computes each order's column from its sample
chain plus its result rows rather than keeping a second status column that could drift. An order
sits at its least-advanced tube — collecting one of two tubes does not move the card.

**Report delivery is blocked by money, structurally.** While a balance is outstanding the
delivery row offers only "Collect due first" — there is no deliver-anyway control to click by
mistake (§7 U7).

## Corrections made during the pass

- Two cross-context LINQ joins (`bill.due` × `reg.patient`, `kernel.approval_request` ×
  `bill.invoice`) threw at runtime — EF cannot join across `DbContext` instances even on one
  connection. Both were split into a query per context with the join done in memory. The module
  boundary (ADR-0003) is real and the query layer has to respect it.
- Dashboard day-window bounds were built as `DateTimeOffset` with a +06:00 offset; Npgsql binds
  only UTC to `timestamptz`. `Ui.DhakaMidnightUtc` now does the conversion in one place.

## Verification

Run against a freshly seeded database on 2026-07-26:

- Every nav route returns 200 for every seeded role (`eng/verify/nav-smoke.sh`).
- Golden thread green end to end: register → serial → order + invoice → pay → labels → collect →
  receive → result (H/L flags) → verify + e-sign → deliver → day-close with a ৳50 variance →
  MD dashboard (`eng/verify/golden-thread.py`).
- Discount escalation, supervisor approval, receipt with amount in words, due collection, and
  refusal of over-collection (`eng/verify/discount-and-dues.py`).
- 80 existing tests still pass; the three UI CI gates pass.

## Cross-verification against the PRD (2026-07-26, after the pass)

Checked the delivered screens against §9A.2 module depth, §9A.4 demo criteria, §7 U1–U15,
§11 state machines, §12 permissions and `05-ui-architecture.md` §5's 16-screen list.

**15 of the 16 MVP screens exist.** Missing: **05 §5 screen 7, refund / cancel request**.
`Invoice.State` carries `Cancelled` and `Refunded`, `CollectAsync` already accepts a negative
amount with `refundOfReceipt`, and the `refund` approval policy is seeded — but there is no
screen, so §11's `Cancelled⚿ / Refunded⚿` exits and module 4's `[M]` "invoice refund with
approval + reason" are unreachable from the UI.

Other verified gaps against explicit PRD `[M]` items or ACs:

| PRD ref | Requirement | Status |
|---|---|---|
| §9A.2 module 7 · §9A.1 F1 | **Bulk price-list import** — "the single strongest lock-in mechanism" | `ImportService` exists (spec 0009); **no screen**. `/admin/masters` is read-only |
| US21.1 | Change a price with an effective date, see who changed it | Rate versions display correctly; **no editing UI** |
| US4.2 AC | "supervisor reopens only with approval" | `reopen` policy seeded, `SessionState.Reopened` exists; **no reopen screen** |
| §9A.2 module 4 | **Referrer capture on every order** | Needs the `adm.referrer` master, which does not exist. The decorative free-text box was removed rather than left discarding input |
| §11 Sample | `Rejected(reason) → Re-collection` | Handler existed with no control; **reject + reason control added to the board during this audit** |
| 05 §6 | Print / Preview / **PDF** — three affordances | Preview + Print only; server-rendered PDF still needs the QuestPDF document set |
| §9A.4 #3 | "populated dashboard with seeded history — never an empty chart" | Not met — 90-day generator is spec 0010 |
| US4.1 AC | One screen for consultation **+ tests** in one invoice | Partial: a diagnostic order raised elsewhere appears on the OPD bill and is swept into it, but the OPD catalogue offers services only |

Deviations from §12 worth recording (small, deliberate): the seeded `Receptionist` lacks the
matrix's `OPD/ER bill R`, and `Pathologist` lacks `LIS results …+ C` (verify only). Seven demo
roles cover the matrix's eighteen — the rest belong to post-MVP modules.

Confirmed **met**: §9A.2 modules 1, 2, 3 (except reopen/refund), 5, 6, 8 at their stated MVP
depth; §11 Invoice, Discount request, Counter session (less Reopened), Test Order, Sample
(now including rejection), Result/Report (less Amended), SMS; §7 U1, U2, U3, U5 (partial — see
type-ahead below), U7, U8, U9, U10 (less PDF), U11, U12, U13, U15.

## Known gaps left open

- **Type-ahead is not yet wired to a search endpoint.** `typeahead.js` exists and the patient
  pickers are `<select>` elements populated with the 50–60 most recent patients. That is fine at
  seed volume and wrong at §14 volumes — the trigram endpoint is the next piece (§7 U5).
- **Micro-help (`?` panels, §7 U14)** is not built; the screens carry inline hint text instead.
- **Amendment and patient-merge flows** have services but no screens (pre-agreed cut list, 08 §4).
- **Seeded 90-day history** is still spec 0010's deliverable; the dashboard is honest but thin
  until it lands.
- **PDF fallback** currently means the browser's print-to-PDF of the same layout; the QuestPDF
  server path (ADR-0009) is wired only for the Bangla spike document.
