---
name: qa-lifecycle
description: Run the patient-lifecycle regression suite against an environment and report a triaged result. Use when asked to test the app, run a regression or smoke test, verify a deployment before or after a release, or check whether a change broke the patient lifecycle.
---

# QA lifecycle

Runs `docs/qa/patient-lifecycle.md` — 175 cases across the built modules, driven by all twelve seeded roles — and reports what held. (`hrm-thread.py`, the HRM SKU's 38-case operator thread, runs separately against :5299 and is not part of these tiers.)

## Invocation

| Typed | Means |
|---|---|
| `/qa-lifecycle` | local, tiers t0 + t1 |
| `/qa-lifecycle local` | same, explicit |
| `/qa-lifecycle t0` | read-only probe only |
| `/qa-lifecycle full` | local, adds t2 — needs a freshly seeded database |
| `/qa-lifecycle prod` | production; **read-only unless the user agrees to writes in chat** |
| `/qa-lifecycle <url>` | any deployment; non-localhost is treated as real |

Delegate the run to the **`qa-lifecycle` agent**, which owns the method and the report format.

## The one rule that matters

Anything that is not localhost is a real deployment. A mutating run there creates patients, invoices, receipts and audit rows that Rule 4 forbids deleting — they can be reversed, never erased. So: **state exactly what will be written and get agreement in chat before running t1 against a non-local target.** Setting `HMS_QA_CONFIRM` yourself, without that agreement, defeats the interlock.

t2 against production is refused unconditionally — it asserts absolute money totals and assumes an empty ledger.

## Tiers

| Tier | What | Writes |
|---|---|---|
| t0 | 12 roles × 64 protected routes, public surfaces, handler-level refusals | no |
| t1 | the ten lifecycle and per-module threads | its own records |
| t2 | `golden-thread`, `discount-and-dues` | needs a fresh DB |

A t1 run that did not log in as all twelve demo users is **incomplete**, not green.

## Before believing a result

- `curl -s http://localhost:5199/health` and `lsof -nP -iTCP:5199 -sTCP:LISTEN` — a leftover instance serves the wrong database.
- `bash eng/check-lifecycle-traceability.sh` — if the document has drifted from the code, the suite is measuring the wrong thing.

Details, including what a production run leaves behind, are in `docs/qa/README.md`.
