import { test, expect } from "@playwright/test";
import { DOCUMENT_ROUTES, authFile } from "../helpers/users";

// Document pages are reachable by more than one role in ROUTES; rasel (Billing Operator) holds
// registration.read + billing.session.close, which covers all four of registration/invoice/
// statement, and lis.worklist.read is needed for /lis/report — ripon has that.
const DOC_USER: Record<string, string> = {
  "/registration/1/card": "jashim",
  "/billing/invoice/1": "rasel",
  "/billing/statement/1": "rasel",
  "/lis/report/1": "ripon",
};

test.describe("U10 — document pages render a letterhead .sheet with a Print control", () => {
  for (const path of DOCUMENT_ROUTES) {
    // Nested describe per iteration: a bare test.use() call repeated in a shared loop applies
    // only its LAST value to every test in the block (all tests resolve the same suite-level
    // fixture override at run time) — nesting gives each path its own scope.
    test.describe(path, () => {
      test.use({ storageState: authFile(DOC_USER[path]) });

      test(`sheet + letterhead + print control`, async ({ page }) => {
        await page.goto(path);

        const sheet = page.locator(".sheet");
        await expect(sheet).toBeVisible();
        await expect(sheet.locator(".sheet-head")).toBeVisible();
        await expect(sheet.locator(".sheet-hospital")).not.toBeEmpty();
        await expect(sheet.locator(".sheet-doc-title")).not.toBeEmpty();

        const printBtn = page.getByRole("button", { name: /print/i });
        await expect(printBtn).toBeVisible();
      });
    });
  }
});

test.describe("Print CSS — chrome hides, .sheet stays visible under print media", () => {
  for (const path of DOCUMENT_ROUTES) {
    test.describe(path, () => {
      test.use({ storageState: authFile(DOC_USER[path]) });

      test(`shell hidden, sheet visible`, async ({ page }) => {
        await page.goto(path);
        await page.emulateMedia({ media: "print" });

        await expect(page.locator(".sidebar")).toBeHidden();
        await expect(page.locator(".topbar")).toBeHidden();
        await expect(page.locator(".status-footer")).toBeHidden();
        await expect(page.locator(".modal-tools")).toBeHidden(); // the Back/Print bar itself is .no-print
        await expect(page.locator(".sheet")).toBeVisible();
      });
    });
  }
});
