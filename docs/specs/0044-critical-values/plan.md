# 0044 — plan

Approved 2026-08-03. One vertical slice: the band gains critical bounds, the flag gains two
values, and the verification screen gains the gate US9.3 asks for.

## WP1 — The band and the flag

`ReferenceBand(Sex, AgeFrom, AgeTo, Low, High)` gains `CriticalLow`, `CriticalHigh`, both
nullable, both **appended** to the positional record so stored JSON without them deserialises to
null rather than failing.

`Flag(value, band)` returns, in this order:

| Condition | Flag | Meaning |
|---|---|---|
| `value <= CriticalLow` | `CL` | panic low |
| `value >= CriticalHigh` | `CH` | panic high |
| `value < Low` | `L` | abnormal low |
| `value > High` | `H` | abnormal high |
| otherwise | `""` | normal |

Critical is tested **first**, so a value that is both low and critically low reads `CL`.
Inclusive bounds (`<=`, `>=`) because a critical list is written as "≤ 7.0 g/dL"; exclusive would
let the threshold value itself through.

A band with no critical bounds behaves exactly as today — that is the URINE-RE / LIPID case, and
it must not start flagging.

Helpers on the flag string (`IsCritical`, `Label`, css class) live next to it so the four screens
that render it cannot disagree about what `CL` means.

## WP2 — Thresholds, and getting them onto existing databases

Critical bounds added only where a panic value is genuinely standard practice — haemoglobin,
white count, platelets, haematocrit, glucose, creatinine. ESR, lipids, TSH and urine microscopy
get none, deliberately.

**These are defaults, and the spec says so.** A lab's critical list is part of its accreditation,
not a vendor's opinion; the seeding comment must point that out so nobody mistakes them for
clinical authority.

`ResultTemplate` gains `Version`. `NeedsUpgrade` currently returns true only when a template has
*no bands at all*, so an existing database — which has bands — would never pick these up.
Comparing a stored `Version` against the current one is the smallest change that makes the
existing `DevSeed` backfill loop do the right thing, and it keeps working for the next template
change too.

Stored values are **not** re-flagged. §7 U12 already stores the flag and the band it was judged
by *with the value*; a released report reproduces itself forever (AC4). Only results entered
after the upgrade see critical flags.

## WP3 — The acknowledgement

`Result` gains `CriticalAckBy`, `CriticalAckAt`, `CriticalAckNote`. Additive nullable columns,
one migration on `lis`.

`CriticalAckNote` stores what was acknowledged — "HB 3.1 g/dL (critical low)" — not just a
boolean. A boolean records that someone clicked; the note records what they were looking at when
they did, which is the thing an audit actually asks.

Not a separate table: the acknowledgement belongs to a *result version*, dies with it on
amendment, and an amendment producing v2 must be acknowledged again on its own values. A side
table would outlive the values it refers to.

## WP4 — The screens

**`/lis/results`** (technologist, first to see it) — a value that flags critical is called out as
it is entered, with the parameter, the value and the band. The technologist cannot verify, but
they are the person who can pick up a phone, and today the screen tells them nothing.

**`/lis/verify`** (pathologist) — the gate:

- Criticals are listed at the top of the order, above the normal parameter grid.
- The verify POST is **refused** while any is unacknowledged, naming parameter and value (AC2).
- Acknowledgement is a distinct, deliberate control — a tick that must be set, not a second press
  of the same button — and posts alongside the values so it cannot be given for a *different*
  set of results than the one displayed.
- On success the note is written to every result version being verified.

**`/lis/report`** — critical renders distinctly from abnormal. A printed report where a panic
value looks like a mild one is the same defect on paper.

**The worklist card** — an order carrying a critical value is marked, so US9.3's *"I focus
attention where it matters"* is true of the queue and not only of the opened order.

## WP5 — Tests

- `Flag()`: critical low, critical high, boundary equality, abnormal-not-critical, normal, and a
  band with no critical bounds (must stay silent).
- Band matching still picks most-specific-first when only some bands carry criticals.
- Template round-trip: JSON without critical bounds parses to null, not an exception (AC4/AC5).
- Screen-level: verification refused unacknowledged, accepted after, note persisted (AC2/AC3).
- **Negative test (G3):** delete the gate, watch AC2's test fail, restore it.

## Traps

- **Do not re-flag stored values.** The temptation is a backfill that "fixes" old results. It
  would rewrite what a pathologist already signed. AC4 exists to forbid it.
- `ResultValues.StoredValue.Flag` is a plain string used by report rendering, the EMR inline
  view and the radiology surface — every consumer must handle `CL`/`CH` or they will render an
  unknown flag as blank, which is worse than `L`.
- `dotnet run` in `src/Hms.Web` binds :5034 without `--no-launch-profile`.
- `DevSeed` only re-applies templates through the existing backfill loop; verify on a database
  that already has the old templates, not only on a fresh one (AC5).

## Deliberately not doing

- Analyzer integration, QC, delta check, reflex rules, send-out tracking — see `spec.md`.
- Telephoning the clinician with a logged read-back. The PRD asks for acknowledgement, not a
  call log; a call log is a real accreditation requirement and deserves its own spec with the PM.
  The `[S]` notification to the ordering clinician is recorded as a follow-up.
- Editing critical bounds through the UI. They arrive with the masters import like every other
  reference range; a per-parameter editor is `/admin/masters` work, not this spec's.
