# 0024 — What "forty operators at once" must mean on 2 vCPU / 3 GB

- **Status:** Proposed — this ADR states a question and a first measurement; it does not decide
- **Date:** 2026-07-28
- **Answers:** lifecycle case `LC-XCUT-11`; `06-deployment.md` §2a; PRD §8 N1, §16
- **Spec:** `docs/specs/0031-lifecycle-coverage-high-gaps/`

## Context

`06-deployment.md` §2a already says it: the verification suite "says nothing about 40 operators
at once". The QA pass of 2026-07-28 confirmed it as a fact about the whole repository — there is
no load test and no concurrency test anywhere, and `LC-XCUT-11` is the highest-severity gap in
the lifecycle register that no amount of test-writing alone closes.

It does not close by test-writing because the question is not "does a test exist". It is:

- **Forty of what?** Forty *named accounts* is a licensing number. Forty *concurrent HTTP
  requests in flight* is a very different load from forty operators typing, where a counter clerk
  spends most of a minute reading a screen and a few hundred milliseconds fetching one.
- **Doing what?** A morning at the billing counter is mostly reads with a burst of writes at
  each save. A day-close is one heavy read. The pharmacy POS type-ahead fires per keystroke.
  These have nothing like the same cost, and a mix chosen for convenience measures nothing.
- **Against what budget?** §8 N1 gives a per-screen response budget. §16 gives 2 vCPU / 3 GB for
  the *whole box* — Postgres, Kestrel, Caddy and the backup job included. Spec 0023 measured the
  app's steady-state RSS; nothing has measured it under concurrency, and the interesting failure
  on a 3 GB box is not latency, it is the connection pool and the page cache.
- **And where does it run?** A load generator that itself needs the CPU competes with the thing
  it measures. On a single VM the generator has to be somewhere else.

## What exists now

`eng/verify/load-probe.py` — stdlib `threading`, N concurrent signed-in sessions replaying a
read-only journey per role, reporting p50/p95/max per route against the §8 N1 budget. It is
**wired into no tier** and it is explicitly not the answer: it measures concurrent *read*
latency and nothing else. It exists so the conversation below starts from a number instead of
an adjective.

First run, twelve operators, three rounds, local development machine (not the target VM):
228 requests, every route p95 under 130 ms, zero budget breaches. That number says nothing about
the 2 vCPU / 3 GB box and should not be quoted as if it did.

## The question, for decision

1. Define the load target in operator terms: **N concurrent sessions, a stated think-time, and a
   read/write mix drawn from a real counter morning** — not N requests per second.
2. Decide what "passing" means: §8 N1 at p95 under that load, and a stated ceiling for RSS and
   Postgres connections on the target box.
3. Decide where the generator runs, since it cannot honestly run on the box under test.
4. Decide whether this belongs in CI at all. A load gate on a shared runner measures the runner.
   A pre-release run against a VM built to the deployment spec is probably the honest form.

## Consequences of leaving it Proposed

`LC-XCUT-11` stays an open High gap in `docs/qa/patient-lifecycle.md`, with this ADR as its
reason, rather than being marked covered by a probe that does not test what the case claims.
That is deliberate: marking it green would be the more damaging outcome of the two.
