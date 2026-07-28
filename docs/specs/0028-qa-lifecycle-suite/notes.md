# 0028 — Notes (afterwards)

## What the first run actually found

Full report in `docs/qa/findings-2026-07-28.md`. The short version is that the **product** held
up and the **harness and the deployment** did not. Nine of eleven threads pass, and all 768
role × route authorization assertions pass on local. The serious findings are:

- production role grants have drifted from the code, to the point where the Billing Operator
  can approve its own discount (F1);
- `appointments.create` is declared and granted but enforced nowhere (F2);
- three of nine t1 threads exhaust finite fixtures — cabin beds, pharmacy stock — and cannot be
  re-run on the same database (F3).

## Two things worth remembering

**A wrong route looks exactly like a permission leak.** The first run of `role-journeys.py`
reported fifty-odd routes reachable without their permission. Every one was a false positive:
Razor `@page` directives override the folder path (`/billing/day-close`, not
`/billing/dayclose`) and several take a route parameter (`/ipd/folio/{admissionId:long}`), so
the probe was hitting 404s — and a 404 is not a denial. Deriving the route from the file path is
wrong; it must come from the `@page` directive. `eng/check-lifecycle-traceability.sh` now
re-extracts both the directive and the `[Authorize]` attribute from source so the table cannot
drift again.

**An authentication failure masquerades as total authorization collapse.** `denied()` returns
true only for a redirect to `/denied`. A session that failed to log in gets redirected to
`/login` instead, which is *not* a denial — so every protected route reads as "reachable" and
the run reports the app wide open. That is why the production probe needed a second look before
anything was reported. The same shape underlies F6: `nav-smoke.sh` treats 302 as success, so a
role redirected wholesale to `/denied` produces a clean table and a zero exit.

Both are the same lesson: in this app the interesting HTTP outcomes are all 302, and a test that
does not distinguish *which* 302 is measuring nothing.

## Deviation from the plan

The plan's `[C]` item — retro-fitting all thirteen existing scripts onto `_harness.py` — was
**not done**. The shared harness exists and the two new scripts use it, but the legacy threads
still carry their own `Session` class and nine of them still hardcode `localhost:5199`.

Reason: the CI `upgrade-path` job invokes nine of them by name, and F3 shows they are already
fragile in ways unrelated to the refactor. Rewriting their plumbing in the same pass that
discovered they cannot be re-run would have made it impossible to tell a refactor regression
from a pre-existing one. The refactor should follow the F3 fixture fixes, not precede them.

Consequence to be honest about: **`--tier t1` against a remote target does not yet work**, because
the legacy threads ignore `BASE_URL`. Only t0 is genuinely environment-aware today. The
interlocks are real and were verified, but they currently guard a tier that cannot reach a
remote host anyway.

## Follow-ups

- Remediation spec for the thirteen High-severity gaps in the register.
- F1: revoke `admin.approvals.decide` from Billing Operator on production; audit the Admin role.
- F2: enforce `appointments.create` in both handlers.
- F3: threads must return the fixtures they take.
- F5: add the eleven missing routes to `eng/verify/ui/helpers/users.ts`.
- Retro-fit the legacy threads onto `_harness.py`, after F3.
