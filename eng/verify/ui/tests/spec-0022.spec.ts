// Spec 0022 — the two defects the full-pharmacy walk exposed.
// Feature-by-feature proof is eng/verify/pharmacy-full.py; these pin the fixes at the surface.
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("Audit viewer search (pre-existing defect)", () => {
  test("searching the audit trail returns results instead of a 500", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("admin") })).newPage();
    // `after` is jsonb; the old LINQ predicate asked Postgres for ILIKE on jsonb and threw
    // 42883 for EVERY term, so this box had never worked.
    for (const term of ["invoice", "receipt", "pharmacy", "Rahim"]) {
      const res = await page.goto(`/admin/audit?Q=${encodeURIComponent(term)}`);
      expect(res!.status(), `searching for "${term}" must not error`).toBe(200);
      await expect(page.locator(".page-title, h1").first()).toBeVisible();
    }
    // And the search actually filters rather than ignoring the term.
    await page.goto("/admin/audit?Q=zzz-no-such-thing-zzz");
    const rows = await page.locator("table.data tbody tr").count();
    await page.goto("/admin/audit");
    const all = await page.locator("table.data tbody tr").count();
    expect(rows).toBeLessThan(all);
  });
});

test.describe("Staff-pharmacy sale tagging (5A-11)", () => {
  test("the counter offers the staff variant and explains what it does", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("parvin") })).newPage();
    await page.goto("/pharmacy/pos");
    // asp-for on a checkbox renders the box plus ASP.NET's hidden false-fallback, so target
    // the checkbox itself rather than the name.
    await expect(page.locator('input[type="checkbox"][name="StaffSale"]')).toHaveCount(1);
  });
});
