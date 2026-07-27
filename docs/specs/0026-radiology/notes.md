# 0026 — Notes

## The decision that made this module small

A radiology report **is** a result. Once that was settled, M10 stopped being "build a reporting
system" and became "build the radiology-shaped surface over the one we have". The report is
written against the same `diag.order_test`, stored in `lis.result`, signed by
`LisService.VerifyAsync` (same e-sign hash), amended through M9's approval-gated path, and
delivered by the existing `/diagnostics/delivery` flow.

The alternative — a `radiology.report` table — would have been quicker to write and would have
given the product two places a patient's report could live, two amendment paths, two audit
trails, and eventually two answers to "what did the report say". The delivery screen would have
needed to know about both. It is the kind of shortcut that reads as progress for a week.

What genuinely belongs to radiology is the **study**: which machine, who shot it, how much film.
That is what the schema holds.

## Small things worth recording

- **A test maps to exactly one machine**, enforced by a unique index. Two machines claiming one
  test would put a patient on two worklists — the mismatched-study failure US10.1 names.
- **Unmapped imaging tests are counted and shown as a warning** on the worklist. A test mapped
  nowhere appears on no worklist, and silence there means a study nobody shoots. Better an untidy
  banner than a missing report.
- **Accession numbers are generated now and unused.** Deterministic from the order test, so a
  study keeps its number. If a DICOM worklist feed is ever built, this is the key the machine
  echoes back; retrofitting one onto historical studies would be far worse than reserving it.
- **Unsigned reports print.** A technician needs a working copy, so the sheet prints marked
  PROVISIONAL with the plain warning not to give it to the patient. US10.2's AC is "cannot print
  as final", not "cannot print".

## Cut, explicitly

[S] DICOM modality worklist feed and [S] PACS integration: the customer owns no DICOM-speaking
device and no image archive. Building an untestable protocol integration would be inventing
capability we cannot demonstrate — the exact thing hard rule 3 forbids. §13 I10 stays Phase 3.
[C] comparison-with-priors is cut too; the patient record (spec 0024) lists prior verified
results, which covers the common need.

## Follow-ups

- Film usage is **recorded, not deducted**. The radiology store is M12's, and inventing a stock
  location now would put film in the pharmacy's outlet where it does not belong. `study.film_size`
  and `film_count` are there for M12 to consume.
- The worklist reads the 300 most recent released orders. Fine at demo and typical volumes;
  it wants a date filter when a real imaging wing's history accumulates.
- The report editor applies the exam's parameter template, which for the seeded imaging tests is
  empty — they are narrative-only. A customer configuring a structured USG template in
  `/admin/templates` gets the grid for free, but nobody has yet tried it with a real one.
