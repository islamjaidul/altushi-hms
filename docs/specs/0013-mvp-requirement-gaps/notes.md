# Notes — 0013

## Outcome

All 25 unsatisfied `[M]`/`Must` rows in `plan.md`'s matrix are closed. Nine new screens
(`/admin/import`, `/admin/people`, `/admin/templates`, `/admin/sms`, `/billing/refund`,
`/billing/reports`, `/lis/amend`, plus editable `/admin/masters` and `/admin/users`), two new
masters (`adm.referrer`, `adm.reporting_consultant`), and two new billing primitives
(`RefundAsync`, `CancelInvoiceAsync`).

## The recurring mistake worth recording

**EF cannot join across two `DbContext` instances**, even inside one `HmsTx` scope on one
connection — it throws at runtime, which means a 500 in the browser rather than a build error.
This bit three times across specs 0012 and 0013 (`bill.due × reg.patient`,
`kernel.approval_request × bill.invoice`, `lis.result × diag.order_test`). It is now a
build-time check: `tests/Hms.Architecture.Tests/CrossContextQueryTests.cs` fails when a LINQ
query expression names two scope contexts. The module boundary (ADR-0003) is real and the query
layer has to respect it — read from one context, join in memory.

## Decisions taken

- **Refunds execute at a counter, not in the approver's inbox.** The supervisor approves; the
  operator with an open drawer carries it out. Money moving from a screen with no cash drawer
  was the wrong shape.
- **Payment in full is the sole lab-release trigger**, from either the counter or a later due
  collection. A part-paid order raises no tube and appears on no worklist, so the bench is never
  asked to work a sample nobody drew.
- **An SMS template with an unknown `{placeholder}` is refused on save.** A typo would otherwise
  mail a literal brace to a patient — a silent, embarrassing failure.
- **Switching an SMS event off queues nothing**, rather than queueing and not sending.
- **A reversed invoice cannot be reversed again.** After a full refund net-paid is zero, which
  looks exactly like an unpaid invoice; without the guard it could then be cancelled too.
- **Editing a report template never touches stored results.** Each result keeps the band it was
  judged by, so an old report reprints exactly as released (edge 22).

## Testing lesson

Ad-hoc HTTP scripts run against accumulated state produced three misleading results in this
spec — a "failing" amendment that was a wrong parameter code, a "missing" reference band that
was HTML-entity encoding, and a broken Playwright fixture caused by an ad-hoc test deactivating
a demo user. The Playwright suite, run against a reset database, was correct every time. New
behaviour belongs there; ad-hoc scripts are for exploration, not verification.

## Deferred, with reasons

Recorded in `plan.md`'s matrix as ⊘: photo capture (no camera on the demo laptop), clinical
intake at registration (belongs with M5 EMR, §9A.3-deferred), token queue and public monitor
(R3, Phase 3), health package billing (needs the package master), corporate billing (M18),
analyzer integration (§9A.2 explicit: manual only), department calendar views (§9A.2 keeps
appointments "lite"), and the distinct revenue/analytics dashboards (5A-20, Should).
