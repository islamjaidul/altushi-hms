# 0021 — Notes (afterwards)

## What the probe found that the happy paths could not

Every defect here needed a patient to do something *other* than get better and go home:
die, abscond, be blocked at the wrong moment, or have an operator click twice. The
lifecycle thread now walks two of those deliberately.

## Decisions

- **"Financially settled" is a fact about the folio, not a flag.** That reframing fixed two
  things at once: terminal exits can be billed, and a patient settled while blocked can still
  be discharged after release. `DischargeAsync` now recognises a locked, invoiced folio rather
  than trusting that some earlier step remembered to set a state.
- **Death and absconding never become "discharged".** Closing their bill leaves the clinical
  state alone; the screen says *close the bill* throughout. A system that labelled a dead
  patient "discharged" because money moved would be lying in a legal document's neighbourhood.
- **Idempotency is a unique index, not a check.** The check makes the common case pleasant;
  the constraint makes it true. The concurrency test asserts the *outcome* (one invoice, both
  callers pointing at it), not which caller won — the mechanism is free to change.

## Surprises

- **My own fix broke the app in a way only the end-to-end run caught.** A form with no token
  binds `Guid.Empty`, which is a *value*: the first tokenless invoice claimed the unique index
  and every later one was refused. Twelve checks failed downstream of a green unit suite.
  `Normalise()` now maps Empty → NULL at the single point where it can reach a column.
- **The first double-click guard broke the cart.** Latching the *form* disabled the save
  button the moment a line was added, because these screens post back to build the cart. It
  now latches the clicked button, and only that. Three UI tests caught it.
- **A latent test-order dependency surfaced**: `NumberSeriesTests` asserted the `invoice`
  series starts at 1, which only held while no other class raised an invoice first. It has its
  own series key now.

## Still open

- **P21**: should refunding a settlement reopen its folio? Current behaviour: no — the refund
  is a reversal on the invoice, and a correcting charge is a new posting.
- A blocked patient who pays in full still needs the R4 release before the gate opens. That is
  R4 working as designed, but the discharge screen could say so more plainly than
  "needs a financially-settled admission" — a wording follow-up, not a defect.
