# 0037 — Plan

## Approved: 2026-08-02

## 1. Make the commit durable (R1) — the root fix

`HrTx.RunAsync` and `HmsTx.RunAsync` both `CommitAsync` without flushing. Both save every attached
context immediately before the commit, so "one business action, one transaction" (G19) means what
it says and a staged change cannot be dropped on the floor.

- `src/Hms.Hr.Web/HrTx.cs` — save `kernel`, `auth`, `hr` before `tx.CommitAsync`.
- `src/Hms.Web/HmsTx.cs` — `TxScope` gains `SaveAllAsync()` over its `_contexts` list; `RunAsync`
  calls it before commit. Contexts are attached lazily, so only the ones the action touched are
  flushed.
- Correct the false comment in `EmployeeService` that taught the wrong model.

Existing mid-body `SaveChangesAsync` calls stay — they exist to obtain generated ids. A final
flush over an empty change tracker is a no-op.

## 2. The two 500s (R4, R5)

- `EmployeeNew.OnPostAsync` — reject a blank `FullName` before the transaction, with the same
  shape as the joining-date check already there.
- `Users.OnPostCreateAsync` — verify the role exists **before** creating the account; if the
  `AddToRoleAsync` still fails, delete the just-created user so nothing roleless survives.

## 3. The roster (R6)

- `OnPostAssignAsync` calls `LoadAsync()` first, so `WeekStart`, `Days` and `Units` are real.
- The form posts `WeekOf` and `OrgUnitId` as hidden fields, so the redirect returns the operator to
  the week and unit they were on.
- Cap raised and made visible: the board says "showing N of M" whenever it truncates.

## 4. The dead link (R7)

`/notifications/tray` is in the shared chrome. The HRM host does not ship the Notifications module,
so the bell is rendered only when the host has that route — driven by the same nav registry the
sidebar uses, not by a hardcoded link in the layout.

## 5. The guard (R8)

`tests/Hms.Integration.Tests` — a test that stages a row inside `HrTx.RunAsync` without calling
`SaveChangesAsync` and asserts it is present after the transaction returns. It fails on the pre-fix
tree; state that it was seen to fail.

## 6. Verification

- `eng/verify/hrm-thread.py` (new in this spec, and what found all of it) green against a fresh
  local database.
- `dotnet build -c Release` clean, full `dotnet test`.
- The ERP's own suite before the ERP is redeployed — the flush touches all fourteen modules.
- Then deploy, and run the same thread against the deployment.
