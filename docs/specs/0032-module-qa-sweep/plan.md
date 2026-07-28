# 0032 — Plan

## Approved: 2026-07-28

The approach the sweep followed, archived per hard rule 0. It is written as it was worked, not
tidied afterwards: where the plan changed mid-flight, the change is stated.

## Shape of the work

A QA loop, repeated per batch of modules:

```
list the modules → find the gaps → hand off to a senior .NET engineer → verify → next
```

The loop is the point. Verification is done by the QA engineer against the **original failing
scenario**, never by reading the diff — a fix that looks right in isolation can still move a
defect somewhere else, which is exactly what happened with M3-D2.

## 1. Inventory

All 22 modules of PRD §5, each marked built or not. The eight with no code (M12–M19) are recorded
as out of scope rather than silently skipped, so the sweep's boundary is explicit.

## 2. Baseline before touching anything

- `dotnet build` + `dotnet test` on the current tree
- fresh `hms` database, app restarted on that build, `lifecycle-suite.py --tier all`

The baseline is what makes a later red meaningful. It immediately paid: `--tier all` was red
**by construction** (QA-H1), which had to be understood before the suite could be used as an
instrument for anything else.

## 3. Per-module gap analysis, from the code

For each built module, read its service and page models, enumerate the rules it actually
enforces, and check each against the three axes — UI smoke, e2e, business logic.

**Derived from the code, not from `patient-lifecycle.md`.** The journey document is the right
instrument for cross-module defects and the wrong one for a module's own unexercised rules. That
choice is what surfaced the register's drift in both directions.

**Anything that looks like a defect is reproduced against the running app or measured on the
database before it is written down.** A finding that cannot be demonstrated is recorded as a
code-read risk and labelled as one (M2-D1 is the example).

## 4. Handoffs, batched and serialised

Batched by theme so one engineer holds one coherent problem; serialised because two engineers
building the same solution produce spurious failures, and because they would race on the QA
documents.

| # | Contents |
|---|---|
| 1 | M1 Registration + the QA-H1 harness fix |
| 2 | M3 queue allocator + the M4/M22 reversal cluster |
| 3 | M2 front desk + M20 notifications + the LC-REG screen path |

Each brief carries: the reproduction, the cause with file and line, why it matters in a hospital,
a recommended fix with the reasoning, what to test, and an explicit invitation to push back.
Engineers do **not** edit `patient-lifecycle.md` or `module-coverage.md` — they report earned row
changes and QA applies them centrally, so the register has one writer.

## 5. Verification, and handing back

Per handoff: build clean, full test suite green with a higher count, **the original repro
re-run**, `--tier all` green on a freshly seeded database, traceability green.

A handoff that introduces a new defect goes back to the same engineer with the evidence. This is
not an exception path — it happened on handoff 2 and it is how M3-D2 was found.

## 6. Register correction and the durable fix

Correct every row the sweep proved wrong, in both directions, then harden
`check-lifecycle-traceability.sh` so the same class of drift cannot recur silently — and
**negative-test the guard** by reintroducing the original bad citation to confirm it fails.

## 7. Close

Final regression across all three layers, `module-coverage.md` completed with resolutions, spec
set to Done.

## Deviations from this plan

- **A fourth workstream appeared:** the register corrections and the traceability hardening grew
  from a side-note into a first-class deliverable once the drift turned out to run both ways and
  to have a single mechanical cause.
- **Playwright was deferred to the final regression** rather than run per handoff. It needs a
  fresh database and a full thread run, and racing it against an engineer's own verification runs
  would have produced false reds in both directions.
- **Scope routed out, not built:** M3's unmet [M] capacity AC went to the PM as P25 under hard
  rule 2 instead of being implemented, and patient merge/deactivation were reclassified as unbuilt
  [S] sub-features rather than test gaps.
