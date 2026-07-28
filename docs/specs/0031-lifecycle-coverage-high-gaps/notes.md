# 0031 — Notes (afterwards)

## Result

Thirteen High-severity gaps at the start; **one** left, and it is left deliberately.

| | before | after |
|---|---|---|
| cases covered | 130 (77%) | 143 (85%) |
| gaps | 39 | 26 |
| of which High | 13 | 1 |

## What the money invariant turned out to be

LC-BIL-11 says "a price change must never alter a historical invoice". Writing the test made the
product's actual shape clear, and it is stronger than the case assumed: `OnPostRepriceAsync`
refuses a start date **before today**, and `Create` opens the first rate version *today*, so a
second price for the same item cannot start before tomorrow. A reprice therefore cannot touch
today's billing at all, let alone yesterday's.

So the assertion is two-sided: the invoice raised at ৳700 still reads ৳700 after the item is
repriced to ৳1,900, the rate history shows v1 **closed** rather than edited, and backdating to
2020 is refused in words. That is Rule 5 as a behaviour rather than a claim, and it is now the
first thread to drive an `admin` session — the reason the case had no proof before.

## LC-DIS-07 is not what the document said

The lifecycle document had it as "Settlement reopened, **approval-gated**". It is not:
`OnPostReopenAsync` checks `ipd.settle` and `ReopenDraftAsync` moves `settlement_draft → open`
with no approval anywhere. Per the escalation rule, the test asserts **what the product does** —
the operator can reopen a draft for a late charge, and the Billing Supervisor, who lacks
`ipd.settle`, cannot — and the discrepancy is recorded here rather than papered over by writing
a test that agrees with the document.

Whether reopening a *confirmed* settlement should be approval-gated is a §12 question for the PM,
not a defect I should decide. Raised in `docs/architecture/09-questions-for-pm.md`.

## LC-LAB-08: the amendment is proven by what leaves the screen

An amendment writes v2 **unverified**. So after the correction lands, the test disappears from
`/lis/amend` — which only lists results whose latest version is signed — and reappears on
`/lis/verify` for the correction to be e-signed. That absence *is* the proof of versioning: an
in-place edit would have left a still-signed v1 sitting exactly where it was. The first draft of
this assertion looked for a `v2` pill and failed against a correct product, which is the more
dangerous kind of wrong test.

Also: the corrected value has to be a real parameter code from the test's template
(`Enter at least one corrected value` otherwise), so the thread reads the codes off the form
rather than guessing `HB`.

## Why LC-XCUT-11 stays open

`eng/verify/load-probe.py` ships — stdlib threading, N concurrent sessions, p50/p95 per route
against §8 N1 — and is wired into **no tier**. It measures concurrent read latency and nothing
else. What forty operators means, what mix of work they do, what the pass criteria are, and where
the generator runs given it cannot honestly share the box under test are architecture questions.
Raised as **ADR-0024 (Proposed)**.

Marking the case covered off a read-only probe would have been the more damaging outcome than
leaving it red: the register would have said "proven" about the one thing nobody has measured.

First run for the record — twelve operators, three rounds, **a development laptop, not the target
VM**: 228 requests, every route p95 under 130 ms, zero breaches. That number says nothing about
2 vCPU / 3 GB and should not be quoted as if it did.

## Deviation worth recording

The remediation prompt requires a case's coverage marker to flip **in the same commit as the
code**. The marker flips for all thirteen cases landed in the spec 0028 commit rather than in
0030/0031, because the lifecycle document was still uncommitted when this work started and went
in whole. The binding property holds in the tree — document and tests agree, and
`eng/check-lifecycle-traceability.sh` fails the build if they ever stop agreeing — but the commit
attribution is wrong and is recorded here rather than quietly left.

## Verified

- `eng/verify/money-and-controls.py` green: 9 cases, 0 failed checks, six roles driving it.
- `dotnet test tests/Hms.Integration.Tests --filter ConcurrencyTests` — 4 passed on real Postgres.
- Playwright 245 tests pass, including all eleven previously unloaded routes.
- `--tier t1` green three consecutive times with the new thread in it, 12/12 roles.
- `check-lifecycle-traceability.sh --stats`: 169 cases, 143 covered, 26 gaps.
