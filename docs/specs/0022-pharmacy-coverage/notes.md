# 0022 — Notes (afterwards)

- **The module was in better shape than its test coverage.** Of 41 checks across all seventeen
  matrix rows, 40 passed on the first run: product and company creation, the reorder shortlist
  (verified by selling stock down past the reorder level and watching the product appear),
  supplier payment and replacement, the approval-gated damage write-off, outlet creation, the
  statements and the dashboard all worked. The gap was in *evidence*, not in the build.
- **The audit-search bug is the more serious find**, and it is not a pharmacy bug at all —
  it sat in M21 Admin since spec 0009. Nothing caught it because the Playwright test loaded
  `/admin/audit` without a query, and no thread ever typed in the box. Any test that only
  visits a page will miss every defect that lives behind its inputs.
- The staff-sale defect is a good example of a feature that looks implemented: the checkbox
  existed, the page model bound it, and it changed behaviour — but only along one branch.
