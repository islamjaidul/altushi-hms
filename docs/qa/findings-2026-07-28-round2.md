# QA findings — 2026-07-28, round 2 (post-remediation)

Second pass after specs **0029**, **0030** and **0031**. This document exists to answer one
question per finding from `findings-2026-07-28.md`: **is it actually closed, and what proves it?**

**Environments.** Local: native `Hms.Web` (Debug) on `localhost:5199` against `hms-dev-db`/`hms`,
reset fresh. Deployment: `https://hms.specshipper.com`, redeployed from commit `6b34e36` at
15:30 Asia/Dhaka, then exercised with a **mutating tier-1 run** — the first t1 ever to reach a
deployment, and only after explicit human agreement (`HMS_QA_ENV=prod`, `HMS_QA_CONFIRM`).

**Verdict.** All seven findings are closed. The one High-severity lifecycle gap that remains
(`LC-XCUT-11`) is open **by decision**, recorded as ADR-0024. Round 2 found four new items — two of them real defects in the remediation itself, one a
long-standing harness assumption that only a deployment could expose — all fixed here.

---

## Cross-match against round 1

| # | Finding | Status | What proves it |
|---|---|---|---|
| **F1** | Production role grants have drifted | **Closed** | `grant-drift.py` reproduced all nineteen extra grants read-only, then revoked them through `/admin/users` with a tier-2 `role.revoke` audit event each. Re-read: no extra grant remains. `role-journeys.py` against the deployment: 768 assertions, 0 failures — where it previously reported `/admin/approvals` reachable by `rasel` and 30 routes by `admin`. Detection is now permanent, in tier t0. ADR-0023 records why the seed still does not reconcile. |
| **F2** | `appointments.create` enforced nowhere | **Closed** | Both handlers carry `if (!CanIssue) return Forbid();`. `HandlerPermissionTests` (3 tests) proves it and fails against the previous code. `money-and-controls.py` proves it end to end on a live app: the grant is revoked, `jashim`'s POST to Issue **and** Advance are refused, the queue stays readable, the grant is restored. `KNOWN_UNENFORCED` is empty and the traceability guard now fails on any recurrence. |
| **F3** | t1 exhausts its fixtures, cannot be re-run | **Closed** | `--tier t1` green **three consecutive times on one database**, 12/12 roles, ward census 13/13 free before and after. Every admitting thread returns its bed through `_harness.on_exit`, so a thread that dies at step 7 still hands back what it took at step 2. On the deployment: 13 admissions created, **all 13 in a terminal state**, nothing left open. |
| **F4** | `pharmacy-thread.py` crashes instead of failing | **Closed** | `fixture()` replaces every picker `.group(1)` across six threads; an exhausted fixture now prints `FIXTURE EXHAUSTED — <what>` and the remedy. `lifecycle-suite.py` attaches a crashed script's output tail, so a traceback no longer reports as `FAIL (0 failed check(s))` — which is how two of the six baseline failures had been invisible. |
| **F5** | 11 of 64 protected routes never loaded by a UI test | **Closed** | All eleven are in `ROUTES`; Playwright is 245/245 green including every one of them. Permission and title were read from each page's `@page` directive and `[Authorize]` attribute, never derived from the file path. |
| **F6** | `nav-smoke.sh` cannot fail | **Closed** | `nav-smoke.sh md 'Demo#1234' /registration /billing/opd` → **exit 1**, both routes reported `DENIED`. The same script on routes the user holds → exit 0. The upgrade gate's `awk` filter, which treated every 302 as a pass in exactly the same way, is gone. |
| **F7** | 13 High-severity gaps | **12 of 13 closed** | Coverage 130/39/13-High → **143/26/1-High**. LC-BIL-11, LC-DX-03, LC-BIL-10, LC-BLK-03, LC-DIS-04, LC-DIS-07, LC-LAB-08, LC-ROLE-14, LC-QUE-08 by `money-and-controls.py`; LC-XCUT-09 and LC-XCUT-10 by `ConcurrencyTests` on real Postgres; LC-XCUT-13 by the Playwright routes. **LC-XCUT-11 stays open** — see below. |

### Also verified, since round 1 could not

The round-1 pass had no `dotnet`, so every `xunit` coverage marker was cited rather than observed.
All of it now runs: `dotnet build -c Release` — **0 warnings, 0 errors** (warnings are errors) —
and **154 tests pass** across the four projects (Kernel 22, Integration 105, Architecture 26,
PrintGolden 1). The markers are observed, not cited.

---

## New in round 2

### R2-1 — a teardown could not release a **blocked** admission · High · fixed

Found by clearing the deployment. `_harness.settle_and_discharge` walked summary → clearance →
draft → invoice → discharge, and did none of it when the admission was under an R4 hold: a
blocked admission cannot be discharged, so every step was refused and the helper **returned
normally**. `GWF-04` on the deployment had been held that way since 27 Jul 22:32.

This is the round-1 lesson recurring one level up. F3 was cleanup that never ran; this was cleanup
that ran, was refused, and said nothing. The helper now takes the hold off first through the
proper approval path — raise release, supervisor approves, apply — before discharging.

### R2-2 — a green run did not prove the fixtures came back · High · fixed

Raised by the QA agent on the deployment run, and correct. `_drain_teardowns` deliberately
swallows a failing teardown so a broken cleanup cannot turn a green run red — which means a
`PASS` proved the assertions held, **not** that the ward was whole. The deployment demonstrated
exactly that: the run reported GREEN with six of thirteen beds held by earlier runs.

`lifecycle-suite.py` now takes a ward census before and after any t1 run and **fails the run** if
the ward has fewer free beds at the end than at the start. Verified in both directions: an
admission taken mid-run drops the count from 13 to 12 and would report `LEAKED`; the teardown
restores it to 13.

Six pre-remediation admissions on the deployment (four from 27 Jul 22:32, two from 28 Jul 01:42 —
all hours before this deploy) were closed through the product's own discharge and release paths.
The ward is 13/13 free.

### R2-3 — the run manifest promised by `docs/qa/README.md` is never written · Medium · documented

`docs/qa/README.md` says a mutating run against a shared target leaves a manifest in
`eng/verify/runs/<host>-<runid>.json` listing every id created, and that records are named
`QA-<runid> …`. The plumbing exists in `_harness.py` (`record()`, `tag()`, `write_manifest()`),
but **no thread calls `record()` or `tag()`** — the nine legacy threads pre-date the harness and
name their records themselves (`Lifecycle 51816`, `Edge Death 07211`, `OT Test 08033`). So
`MANIFEST` is always empty, `write_manifest()` returns `None`, and `eng/verify/runs/` does not
exist after a deployment run.

Not fixed here, deliberately: wiring `record()`/`tag()` through nine threads is the plumbing
retrofit spec 0028's notes deferred until after the fixture fixes, and doing it in this pass
would repeat the mistake that deferral exists to avoid. The README is corrected to describe what
a run **actually** leaves — records identifiable by name and by their timestamp window — and the
retrofit is the outstanding follow-up.

Consequence to be honest about: **reversing a production run today means finding its records by
name**, not by reading a manifest.

### R2-4 — `edge-cases.py` issued somebody else's indent · High · fixed

The definitive deployment run failed one check: *"the over-issue is refused with the real
remaining quantity"*. It reads as a product defect — the ward asked for 99,999 units and the
pharmacy did not refuse. It is not.

Case 4 raised its indent and then took **whatever was at the top of the pharmacy issue queue**.
Locally the queue is usually empty, so the top was its own. On the deployment thirteen indents
from earlier runs were waiting, so it issued a stranger's small indent, which succeeded, and the
over-issue the case exists to prove was never attempted.

`ipd-thread.py` step 6 already carried the guard for exactly this — *"on a database with history
the queue holds other wards' indents too — issue OUR one, identified as the id that appeared
after we raised it"*. `edge-cases.py` did not. It does now.

This is the sharpest illustration of why running against a deployment matters: a whole class of
"first row wins" assumption is invisible on a database only the test has ever touched, and
certain on one a hospital has used. Worth a sweep of the remaining threads for the same shape.

**Side note.** Those thirteen indents are themselves an unreturned fixture. Unlike a bed, a
pending indent blocks nothing, so it is not a ratchet — but it is queue clutter that grows by a
few rows per run, and the retrofit in R2-3 is where it would be addressed.

---

## The one High gap left, and why

**LC-XCUT-11 — no load or concurrency test.** `eng/verify/load-probe.py` ships as a first cut
(stdlib threading, N concurrent sessions, p50/p95 per route against §8 N1) and is wired into **no
tier**. It measures concurrent read latency and nothing else.

What forty operators means, what mix of work they do, what "passing" is, and where the generator
runs given it cannot honestly share a 2 vCPU / 3 GB box are architecture questions — raised as
**ADR-0024 (Proposed)**. Marking the case covered off a read-only probe would put "proven" in the
register next to the one thing nobody has measured, which is worse than leaving it red.

---

## What the deployment run left behind

A tier-1 run against a real deployment is designed to be identifiable and reversible, never
erasable (Rule 4). This one created 13 admissions, all closed, plus registrations, invoices,
receipts and lab orders under the usual thread names.

The definitive run after every round-2 fix: **10/10 scripts, 0 failed checks, 12/12 roles, ward
census 13 free before and 13 after.**

**Cannot be undone, by design:** `kernel.audit_event` rows, `ipd.bed_day` rows,
`pharm.stock_move` ledger entries, consumed number-series values (UHIDs `ALT-…`, admissions
`ADM-2026-27-…`), and any SMS the gateway actually dispatched. The dashboard carries the day's
activity until it is reversed or the day rolls.

A pre-deploy dump was taken first: `/root/hms-predeploy-20260728-0831.dump` on the VM.

## What is still not verified

- **The deployment under load.** The probe ran on a development laptop. Nothing has measured the
  2 vCPU / 3 GB box under concurrency — that is LC-XCUT-11, above.
- **The go-live switch (RUNBOOK §9).** Rehearsed, never executed. The demo cast is still live
  with `Demo#1234`, which is what makes a t1 run against this deployment possible at all; once it
  is executed, t1 against production stops working, by design.
- **The manifest path**, until R2-3's retrofit lands.

---

## Round 3 — what a *well-used* database exposed (2026-07-28, later still)

The three-consecutive-runs bar in spec 0029 was met on a **freshly reset** database. Running the
same bar again on a database that had by then absorbed roughly twenty suite runs found five more
defects, every one of them the same shape as R2-4: **an assertion that takes the first row of a
list that now has history.** None was a product defect. All are fixed.

| # | Where | What it did | Fix |
|---|---|---|---|
| R3-1 | `ot-thread` consumable picker | Summed stock by the **first word** of the product name. The pharmacy probe creates products called `Probecillin <stamp>`, so a dozen distinct products pooled into one "Probecillin" total of 438 units, and the thread then consumed a product whose own batches were all disposed | Key on the whole product label, and scope the shelf to the **main** outlet, which is the only one an OT case draws from |
| R3-2 | `pharmacy-thread` step 7 | Quarantined and returned *whatever expired batch was lying about* — on a used database, a stranger's batch, in another outlet, sometimes already returned — then checked **this run's** supplier ledger for the credit | Quarantine and return the batch **this run received at step 2**, from the supplier **this run bought from** |
| R3-3 | `pharmacy-thread` GRN | Every run received a batch called `THREAD-1`, so "find our batch" found the oldest and emptiest one | A per-run batch number, `THREAD-<stamp>` |
| R3-4 | `pharmacy-thread` FEFO | Asserted **which** batch FEFO picked. Once two batches share an expiry — which happens the second time the thread runs — that is an arbitrary tie-break the product never promised | Assert the promise: the batch on the receipt expires on the earliest date on the shelf |
| R3-5 | `pharmacy-full` / `pharmacy-thread` | Both used the quarantine reason *"damaged in transit"*, so each script's assertion could pass on the **other's** row while its own quarantine had silently failed | A reason unique to the run; and both quarantine/return posts now carry `OutletId`, without which the credit landed in an outlet the check could not see |

Two smaller ones alongside: `ot-thread` spread its run dates over 200 days, so two runs collided
on a shared surgeon often enough to matter (now ~3000, and it *searches* for a free slot rather
than assuming the first is free — finding one is setup, not the assertion); and `emr-thread` had
a local variable named `record` that shadowed the harness helper.

**The lesson, and it is the same one three times over.** A test written against a database only
that test has ever touched encodes "the first row is mine". It stays green for as long as that is
accidentally true. The deployment made it false, and so does any hospital. Spec 0029's bar —
three consecutive runs — is necessary and was **not sufficient**: three runs on a clean database
does not produce the accumulation that breaks these. The bar worth keeping is **three consecutive
runs on a database that has already been used heavily**, which is what these fixes were verified
against.

---

## Round 4 — the QA agent audited my own remediation, and found three things (2026-07-28)

The deployment lifecycle run after the P24/R2-3 work came back **GREEN — 10/10 scripts, 12/12
roles, ward census unchanged**. The agent was asked to verify the *new* behaviour independently
rather than trust the green, and two of the four claims did not hold.

| # | Finding | Verdict | Fix |
|---|---|---|---|
| R4-1 | `money-and-controls.py` creates five patients, admissions, invoices and receipts a run and called **neither `tag()` nor `record()`** — so on production the one script that moves money left records no manifest listed and no name identified as QA's. The agent found three earlier runs' worth of the same untagged pattern already on the host | **Confirmed — my omission.** The script was written before the manifest existed and I never went back for it | Tagged and recorded, like every other thread |
| R4-2 | `lifecycle-suite.py` never put `HMS_QA_RUN_ID` in the subprocess environment, so each of the ten scripts fell back to its own timestamp: **five manifest directories for one run**, contradicting the commit that shipped it | **Confirmed — my bug.** I wrote the harness to read the variable and never wrote the line that sets it | The runner exports its run id; one invocation is now one directory |
| R4-3 | The P24 audit row's payload showed only `{"state": "open"}` — "not the before/after image the commit claims" | **Half right, and the useful half.** The row *does* store both, at tier 1: `before {"state": "settlement_draft"} → after {"state": "open"}`. What was wrong is the **audit viewer**, whose `AuditRow` never selected `Before`, so no tier-1 event's before-image had ever been visible on the screen the audit trail exists to serve | `/admin/audit` now renders `before → after` |

R4-3 is the one worth dwelling on. ADR-0011 makes the before-image the defining feature of tier 1,
and the viewer had been silently dropping it for every tier-1 event in the system since it was
built — rate changes, invoice cancellations, permission changes, all of them. A supervisor asking
*"what did this change **from**?"* — the entire question a withdrawn settlement or a repriced item
raises — could not be answered from the screen. It took an agent reading the payload against the
claim to notice, because every existing test asserted only that the *action* appeared.

**The pattern across rounds 2, 3 and 4 is one pattern:** a check that confirms the thing it was
built to confirm, and nothing around it. The suite verified that an audit row exists, not what it
says; that a teardown ran, not that it worked; that a thread's assertion passed, not that it
tested the row it thought it did.
