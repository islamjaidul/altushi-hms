# 0031 — Plan

## Approved: 2026-07-28

## Approach

### F5 — the eleven routes

`ROUTES` in `eng/verify/ui/helpers/users.ts` is a hand-maintained table of
`{ path, permission, user, title }`. The eleven detail screens take a route parameter, so each
needs an id that exists on a seeded database; the suite already does this for
`/billing/invoice/1` and `/lis/report/1`. Permission and title come from each page's `@page`
directive and `[Authorize]` attribute — read from source, never guessed, because deriving a route
from its file path is the exact mistake spec 0028's notes record.

### F7 — one new thread, plus two real-Postgres tests

**`eng/verify/money-and-controls.py`** (tier t1) — a single thread because these cases share a
patient and an invoice, and because the alternative is eight scripts that each re-provision the
same fixture. Cases, in the order they can actually be performed:

| Case | Who | Assertion |
|---|---|---|
| LC-BIL-11 | `admin`, `rasel` | invoice at price A → `admin` reprices to B → the **old invoice still reads A**, a new one reads B |
| LC-BIL-10 | `rasel` → `shahid` | cancellation is a reversal: the invoice survives, cancelled, with a reversing entry — no row disappears (Rule 4) |
| LC-DX-03 | `rasel` | a partly-paid order is **not** on the LIS board; paying the balance releases it |
| LC-BLK-03 | `jashim`, `shahid` | applying a block **without** an approval is refused; the same for release |
| LC-DIS-04 | `rasel` | discharge with a due needs a typed reason, and writes a tier-2 audit event |
| LC-DIS-07 | `rasel` → `shahid` | a confirmed settlement reopens only through an approval |
| LC-LAB-08 | `farhana` | amend after verification versions the result rather than overwriting it |
| LC-ROLE-14 | `admin` | a permission revoked mid-session stops working within the security-stamp window |

LC-ROLE-14 is the one that mutates the grant matrix, so it **restores the grant it revokes** in a
teardown registered with `_harness.on_exit` — spec 0029's rule applies to permissions exactly as
it applies to beds.

The thread uses `_harness.py` from the start: it is new code, so none of spec 0028's
"don't refactor and fix in one pass" reasoning applies.

**`tests/Hms.Integration.Tests/ConcurrencyTests.cs`** — real Postgres via the existing
`PostgresFixture` (G7):

- **LC-XCUT-09** — power cut mid-transaction. A unit of work that writes several rows and then
  throws leaves **nothing** behind: no folio line, no ledger row, no number-series consumption
  that cannot be re-derived. Proven by asserting the row counts either side.
- **LC-XCUT-10** — two operators on one folio. Two connections post to the same admission
  concurrently; both postings survive and the folio total is the sum — or one is refused with a
  plain message. What must not happen is a lost update.

**LC-XCUT-11** — `eng/verify/load-probe.py`, stdlib `threading` only (G15 — no new dependency for
a 2 vCPU / 3 GB box). N concurrent logged-in sessions replay a read-heavy operator journey and it
reports p50/p95 per route against §8 N1. It is **not** wired into any tier: what N should be,
which mix of writes, and what happens to the 3 GB budget under it are questions for the
architect. Recorded in `docs/architecture/09-questions-for-pm.md` is the wrong place — this goes
to the architect as an open ADR question.

## Risks

- **A thread that reprices a catalog item changes a shared fixture.** Repricing is
  effective-dated (Rule 5), so it cannot corrupt history by construction — that is the very thing
  the case asserts. It creates its own service to reprice, so no seeded price moves.
- **LC-ROLE-14 revokes a live grant.** On a shared target that is disruptive for the seconds it
  lasts. It runs against a role no other case in the run depends on, and restores in teardown.
- **Marking a case covered that only nearly passes.** The rule stands: no marker flips for a test
  not observed passing. Anything that does not pass stays a gap with its reason recorded.

## Verification

- Playwright: the eleven routes load under their own role.
- `python3 eng/verify/lifecycle-suite.py --tier t1` green, including the new thread, three times.
- `dotnet test tests/Hms.Integration.Tests` green, including the two concurrency tests.
- `bash eng/check-lifecycle-traceability.sh --stats` green with the reduced gap count.
