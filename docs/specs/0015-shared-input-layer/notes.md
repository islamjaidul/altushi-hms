# 0015 — Notes

- **Verification scripts were themselves coupled to the old picker.** `golden-thread.py`,
  `discount-and-dues.py` and Playwright's `findPatientId` all scraped the patient `<option>`
  markup; they now resolve patients through `/api/typeahead/patients` — which is truer to how
  an operator works and exercises the new endpoint on every run.
- **`discount-and-dues.py` asserted by patient name, not invoice id** — correct on a fresh
  database, ambiguous on a populated one. Rewritten to track the invoice it creates. That is
  what made it usable as the upgrade gate's workflow probe (ADR-0022); `golden-thread.py`
  still asserts absolute figures (income = ৳550) and stays fresh-DB-only — making it relative
  is a small follow-up if the gate ever needs the full thread.
- **Reports period/date coupling**: the fix is two-sided — server (any parsed date forces the
  custom range) and client (editing a date flips the dropdown; picking a period clears dates
  then submits). The inline `onchange` submit had to move into `hms-date.js`, because it fired
  before the document-level clear handler.
- Client date echo in `hms-date.js` deliberately mirrors, not replaces, the server parser —
  the server (`FlexibleDate`) is authoritative; an unrecognised entry is marked
  `.input-invalid` and left for the server to refuse.
- Type-ahead endpoint ranks prefix matches first via a boolean `ILIKE` order — good enough at
  §14 volumes with the existing trigram index; revisit with `similarity()` only if ranking
  quality complains.
- Follow-ups: barcode-wedge + micro-help capabilities of 05 §3 remain unbuilt (tracked MVP
  debt); catalog/doctor/supplier type-ahead sources land with the modules that need them
  (M11 first).
