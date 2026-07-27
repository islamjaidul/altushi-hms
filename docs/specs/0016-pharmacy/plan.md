# 0016 — Plan

## Approved: 2026-07-27

## 1. Traceability matrix (PRD → surface; built first, per DoD rule 1)

| # | PRD item | MoSCoW | Surface | Status |
|---|---|---|---|---|
| 1 | §5 M11 company & product registration | [M] | `/pharmacy/products` — companies, products (brand/generic/strength/form/unit), reorder level | **Build** |
| 2 | §5 M11 PO → receive → purchase return | [M] | `/pharmacy/purchase` — §11 PO states, approval-gated approve, GRN creating batches (batch/expiry/qty/cost/MRP), return-to-supplier | **Build** |
| 3 | §5 M11 outdoor sale (FEFO, expired blocked, receipt) | [M] | `/pharmacy/pos` — POS on the Opd.cshtml pattern; sale → encounter(kind `pharmacy`) + charge lines + invoice + receipt via `BillingService`; FEFO allocation under `FOR UPDATE` | **Build** |
| 4 | §5 M11 indoor issue → folio (US11.2) | [M] | — | **Deferred: M6 folio does not exist** (Wave 2, `11-build-plan-phase2.md` §1). The M6 spec's indent screen consumes `StockService.IssueAsync` unchanged. |
| 5 | §5 M11 sales return & company return | [M] | Sales return: `/pharmacy/pos` restock path driven by the existing refund approval (invoice reversal restocks via sale allocations). Company return: `/pharmacy/stock` (quarantine → return-to-supplier with ledger credit) | **Build** |
| 6 | §5 M11 expiry management | [M] | `/pharmacy/stock` — near-expiry filter (configurable window), expired auto-quarantine, sale-block in `StockService` | **Build** |
| 7 | §5 M11 auto reorder shortlist (US11.3) | [M] | `/pharmacy/reports` — ROL vs on-hand vs 30-day velocity | **Build** |
| 8 | §5 M11 supplier ledger & payment; customer due ledger | [M] | `/pharmacy/suppliers` — payable from receipts, payments, replacement credits. Customer dues: existing `/billing/dues` (pharmacy invoices are bill invoices) | **Build** |
| 9 | §5 M11 statements + stock ledger + stock audit⚿ | [M] | `/pharmacy/reports` (sales/purchase by range on the hms-date contract; stock ledger from `stock_move`) + `/pharmacy/stock` audit flow (§11 states) | **Build** |
| 10 | §5 M11 pharmacy dashboard (US11.4) | [S] | `/pharmacy/dashboard` — today's takings, stock value at cost, near-expiry count, short items; MD sees pharmacy income via the money spine's department split | **Build** |
| 11 | §5 M11 discharge-time medicine return | [S] | — | **Deferred with M6** (needs folio settlement) |
| 12 | §5 M11 multiple counters/sub-stores + transfer | [C] | `/pharmacy/transfers` | **Build** (pulled up by 5A-11) |
| 13 | 5A-11 outlets master + transfer indent/transfer/outlet ledger | Should | `/pharmacy/transfers` — outlets, indent → send → receive, per-outlet ledger view | **Build** |
| 14 | 5A-11 damage management | Should | `/pharmacy/stock` — damage quarantine + approval-gated write-off (Disposed, logged) | **Build** |
| 15 | 5A-11 expired-medicine management | Should | covered by #6 + quarantine → return/dispose exits | **Build** |
| 16 | 5A-11 supplier replacement | Should | `/pharmacy/suppliers` — return-for-replacement: credit + replacement receipt linked | **Build** |
| 17 | 5A-11 POS variants: outdoor / staff / indoor / outdoor-transfer | Should | Outdoor = #3. Staff = sale tag + discount via approval engine (P17 pending). Indoor = deferred with #4. Outdoor-transfer = transfer flow #13 | **Build** (partial: indoor variant deferred with M6) |

## 2. Decisions

ADR-0021 (written with this spec): `pharm` schema owned by `Hms.Pharmacy`; **stock ledger =
append-only `stock_move`**, batch quantity maintained under `FOR UPDATE` with `qty >= 0`
CHECK; **FEFO** issue; valuation at batch cost; sales ride the **bill spine** via counter-sale
encounters (kind `pharmacy`) so numbering/dues/refund/day-close/dashboard need no new code;
batch MRP is the price source, snapshotted on the charge line (hard rule 5).

## 3. Build order

1. `Hms.Pharmacy` + `Hms.Pharmacy.Contracts` projects; `PharmDbContext` (companies, products,
   suppliers, outlets, purchase_order + lines, batches, stock_move, sale_allocation,
   transfer + lines, supplier_ledger, stock_audit + lines) + migration; wire into `HmsTx`,
   `Program.cs`, solution, DevSeed (pharmacist `jashim2`? — no: dedicated `parvin`,
   Pharmacist role per §12; demo products/batches incl. one near-expiry, one expired).
2. `StockService` (receive, FEFO allocate/issue under lock, quarantine, writeoff⚿, transfer,
   audit⚿) + `PurchaseService` + integration tests (FEFO, expired block, concurrent last-strip,
   negative-stock CHECK, PO lifecycle, audit adjustment).
3. Screens in the family shapes (memory: list/POS/pipeline/document): products, purchase, pos,
   stock, transfers, suppliers, reports, dashboard. `Perm.Pharmacy*` + `ModuleNav` + §12 role.
4. `eng/verify/pharmacy-thread.py` (receive → sell FEFO → expired blocked → due → refund
   restocks → audit) + Playwright `spec-0016.spec.ts` + refresh upgrade fixture note.
5. Full verification: 3-level tests, harness, upgrade gate, CI greps. Close spec.

## 4. PM questions raised

P17 (staff-pharmacy pricing policy) appended to `09-questions-for-pm.md`.
