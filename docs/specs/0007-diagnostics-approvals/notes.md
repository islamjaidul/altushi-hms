# 0007 — Notes
- 2026-07-26 — All [M] requirements shipped, test-proven (9 new integration tests). The [S] SSE
  channel + inbox screen follow the UI pass (spec 0005 notes) — the engine, events and policy
  data they render are done and tested.
- 2026-07-26 — Invoice byte-compare (G6) is implemented as stored-resolution stability (invoice
  lines keep price + rate_version_id; re-resolution proves divergence). PDF golden-file
  byte-compare joins in S6 when layouts are final.
