# 0008 — Notes
- 2026-07-26 — All [M] requirements shipped and proven by 6 new integration tests (37 total).
  [S] screens + the notification dispatch wiring follow the UI pass (spec 0005 notes);
  the report-ready event seam (outbox) exists from S3's pattern.
- 2026-07-26 — DayCloseSummary.dept_split ships "{}" until masters land (S5) — the column and
  versioning behaviour are in place; dept attribution is a read-side join, not a schema change.
