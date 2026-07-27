# 0023 — Notes

## What the generator produced

90 days, generated in **11m46s** through the real services. Row counts on the finished database
(112 MB), and the §14 "typical/day" band each one lands in:

| Rows | Count | Per day | §14 typical/day | In band |
|---|---|---|---|---|
| Patients | 9,101 | ~100 | 80–150 | yes |
| Invoices | 35,363 | ~390 | 150–350 OPD + 150–400 pharmacy | yes (both streams) |
| Charge lines | 84,957 | ~940 | — | — |
| Receipts | 35,363 | ~390 | — | — |
| Test orders | 8,190 | ~90 | 150–350 | **below band** |
| Results | 11,206 | ~124 | 400–1,000 samples | **below band** |
| Stock moves | 24,433 | ~270 | — | — |
| Admissions | 1,009 | ~11 | 10–25 | yes |
| Audit events | 84,882 | ~940 | — | — |

**Stated honestly:** diagnostics volume is under the §14 band. The generator orders tests on 90
of the 220 daily invoices with 1–3 tests each; §14's 150–350 orders/day would need a second
diagnostics-only stream. The shape is right and the joins are exercised; the absolute diagnostic
row count is roughly a third of a busy wing's. For a memory measurement that is a conservative
error in the wrong direction, so it is recorded rather than papered over — the number to raise if
a future measurement needs the full band.

## The measurement

Recorded in `06-deployment.md` §2a. Headline: app **82 MB at rest / 253 MB peak**, Postgres
**220 / 222 MB**, **475 MB combined peak**, slowest page 244 ms.

**Abort-criterion verdict: clear.** `11-build-plan-phase2.md` §2.9 stops the build if sustained
RSS passes 2.2 GB on the VM profile. The measured peak is 475 MB — about 4.5× headroom — and
`measure-rss.sh` now exits 2 if the line is ever crossed, so the criterion can be evaluated at
every wave instead of asserted.

Both estimates in the original budget table were **pessimistic**, which is the right direction to
have been wrong in. The app's estimate (250–400 MB steady) turns out to be its *loaded* figure,
not its resting one.

## Two defects the rehearsal found

Rehearsing a procedure is not paperwork. Running RUNBOOK §9 for the first time found two things
that would have stopped a real go-live at 2 a.m.:

1. **`/admin/users` had no way to change a password.** Step 2 says "set a strong unique password
   per account that stays". The screen could create accounts and deactivate them, and nothing
   else. The procedure had been reviewed and approved by people (including me) reading it rather
   than doing it. Fixed: a per-row reset that bumps the security stamp and writes a tier-2 audit
   fact; the new password itself is never audited.

2. **The go-live gate "zero provisional prices" could never be cleared.** The percentage-marker
   service (`IPD-SVC-PCT`) was seeded with no rate version because the seeder skipped rates where
   `price > 0` was false. The masters screen counts *unpriced* items as provisional, so a
   deliberately free item read as an unfinished one, forever. An item priced at zero is a priced
   item; the rate version is now written regardless, outside the creation branch so upgraded
   databases gain it too.

## Deliberately not done

**The go-live switch has not been executed on production.** P16 (who owns the cutover, and when)
is unanswered, and firing it would disable the demo seed and rotate the shared password on the
live URL. This spec makes the procedure executable and proven; the trigger stays with the PM, as
the runbook says. Everything except that instruction is now removed as an excuse.

## Surprises and follow-ups

- **The Razor build server goes stale after `.cshtml` edits** and reports phantom syntax errors
  in untouched files. `dotnet build-server shutdown` clears it. Third time this has cost time
  (spec 0017 first hit it); worth a note in the README's dev section.
- **A leftover app instance on the measurement port silently served the wrong database** — the
  same failure mode as spec 0022's. Both new scripts now refuse to boot over an occupied port,
  and `measure-rss.sh` runs the built DLL under `exec` so the process it samples is the server
  and not `dotnet run`'s launcher.
- **Page timings were being measured on redirects.** The bash login was posting `UserName`
  instead of `Username`, so every "page" timing was a 302 to `/login` at ~1 ms. Split into
  `eng/verify/page-timings.py`, which asserts it landed on a page and fails otherwise. A
  suspiciously good number is a bug report.
- Follow-up: run `MODE=vm eng/verify/measure-rss.sh` on the VM at each wave's deploy and append
  the figure; the Mac number is the fast loop, the VM number is what the budget is judged on.
- Follow-up: the diagnostics stream should grow a counter-originated order path if a future
  measurement needs the full §14 band.
