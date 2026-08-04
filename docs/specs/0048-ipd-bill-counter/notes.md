# 0048 — Notes

- 2026-08-04 — Spec created with plan pre-approved (demo-day burst 0046–0050).
- Sessions were already unique per **counter** (not per operator), which made the dual-drawer
  model cheap: the session page grew a second slot (one outdoor + one IPD per operator)
  instead of a close-and-reopen dance. Day-close picks the drawer via `?Kind=ipd`.
- Deliberately kind-agnostic, recorded: dues/refunds (the IPD counter may collect IPD dues)
  and folio advances (taken wherever the family is standing; they stamp whichever session
  took them and reconcile in that session's day-close). Splitting the dues queue by
  OPD/IPD parentage remains open.
- Pharmacy POS and diagnostics ordering now resolve outdoor-only sessions too — an IPD-only
  drawer can no longer mint encounters anywhere, not just on /billing/opd.
