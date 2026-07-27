# 0016 — Tasks
- [x] Projects + PharmDbContext (15 tables, ck_batch_qty CHECK) + InitPharm migration + HmsTx/Program/solution wiring
- [x] StockService (FEFO under FOR UPDATE, quarantine/return/dispose⚿, transfers, audit⚿) + PurchaseService (§11 PO machine)
- [x] Screens ×8: pos, stock, purchase, products, transfers, suppliers, reports, dashboard
- [x] Perm.Pharmacy* + ModuleNav (8 entries) + Pharmacist role + parvin + pharmacy counter + demo stock (fresh/near-expiry/expired) + entitlement regenerated
- [x] Refund/cancel restocks via sale allocations (sales return); walk-in patient; credit-needs-a-name rule
- [x] 7 integration tests (FEFO, expiry blocks ×2, last-strip concurrency, batch machine, PO machine, audit⚿)
- [x] pharmacy-thread.py (10 steps, dirty-DB tolerant) + spec-0016.spec.ts + route matrix/authz pairs (140 Playwright tests green)
- [x] Upgrade gate extended with pharmacy; caught + fixed a real upgrade bug (counter seed not additive)
- [x] Final verification: 91 .NET tests · 3 threads green on fresh DB · 140 Playwright · upgrade gate · 4 CI greps
