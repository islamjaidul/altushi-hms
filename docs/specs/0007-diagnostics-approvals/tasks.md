# 0007 — Tasks
- [x] Approval engine: Raise (auto-approve under role threshold, snapshot of routing chain), Decide (state-guarded, double-decision safe), delegation windows (P4)
- [x] Policy data seeded as §12 workflow rows (discount: operator ৳200 → supervisor → MD)
- [x] Rate versions + GiST exclusion constraint (btree_gist); overlap rejected by DB
- [x] Rate resolution: package > corporate > standard, effective-date honoured; comprehensible miss error
- [x] Edge 13: price change = new version; historical day resolves to old version id + price
- [x] Diagnostic order → unbilled charges via IChargePoster (referrer/doctor attribution stored)
- [x] Order machine: ordered→invoiced→in_progress guarded (0-rows = comprehensible error, G9)
- [x] TestOrderPaid outbox event in the payment transaction
- [ ] SSE unbilled-charge channel + approvals inbox screen — deferred to UI pass (notes)
