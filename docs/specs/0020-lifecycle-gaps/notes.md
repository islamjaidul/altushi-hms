# 0020 — Notes (afterwards)

## Why these defects survived every earlier test

Each module's tests exercised that module with data the test itself created. All three defects
live **between** modules or between an operator's habit and a stored format:

- the phone was stored by registration and searched by five other screens;
- the settlement was written by billing and released by IPD;
- the sale was made by pharmacy for a patient owned by IPD.

The lasting fix is therefore not the three patches but `eng/verify/lifecycle-thread.py`: one
patient, seven roles, every module in one run, asserting what one module wrote is what
another sees. It is in the upgrade gate, so it runs against restored production-shaped data too.

## Decisions

- **Phone matching is a database-generated column**, not app-side normalisation on read or a
  second write path. `regexp_replace(coalesce(phone,''), '\D', '', 'g')` is immutable, so
  Postgres keeps `phone_digits` exact forever and back-fills every existing row at migration
  time. The digits predicate only engages for terms containing ≥4 digits, so name searches
  are untouched and the index stays useful.
- **Discharge-with-a-due is permitted but never silent.** §3.2's payment culture makes dues
  legitimate (corporate credit, "family pays tomorrow"), so a hard block would be wrong and
  would stall the gate. Instead the whole outstanding position is shown, the one-click button
  is absent while money is owed (§7 U7), and the release requires a typed reason that lands in
  the tier-2 audit stream with the operator's name. P20 asks the PM whether they want it
  stricter (approval-gated); making it so is a policy row, not a rewrite.
- **The admitted-patient banner warns rather than routes.** An attendant buying at the counter
  is a real transaction; forcing it to the folio would break the counter. Routing belongs with
  5A-11's POS-variant selector.

## Surprises

- **A stale Roslyn/Razor compiler server produced 402 phantom errors** in files nobody had
  touched, including a clean checkout of the committed tree — while the same commit built
  perfectly in Docker. `dotnet build-server shutdown` fixed it instantly. Worth knowing before
  anyone spends an hour "fixing" valid Razor. (The Folio page was refactored off tuple
  deconstruction while chasing it; that change stands on its own merits — named records read
  better in a view than positional tuples — but it was not the cause.)
- **The verification harness was not as dirty-DB-tolerant as it claimed.** Repeat runs failed
  three different ways: the registration duplicate guard (edge 23) withheld saves for
  same-shaped names, admitting scripts left beds in `Cleaning` until the ward ran out, and
  `pharmacy-thread` asserted a hard-coded batch number that repeated runs consumed. All three
  are fixed — the scripts now acknowledge the guard as an operator would, hand beds back, and
  assert the FEFO *property* instead of a fixed batch. Four consecutive passes on one database
  are now green.
- The duplicate guard firing on test data is the product working correctly; the fix belonged
  in the harness, not the app.

## Follow-ups

- P20 (discharge-with-due: reason vs approval) — one policy row either way.
- Routing an admitted patient's counter sale to the folio, with 5A-11's POS variants.
- The bed `Cleaning → Free` step has no owner in the demo data; if housekeeping is not a real
  role at the customer, beds will silently pile up in Cleaning. Worth a PM question when M16
  (staff registry) lands.
