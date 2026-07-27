// Spec 0015 — the shared input layer (ADR-0020). These assert the *contract*, not one screen:
// dates always win over the period dropdown, search predicates reach records of any age, and
// every patient picker is the shared type-ahead bound to /api/typeahead/patients.
import { test, expect, request } from "@playwright/test";
import { authFile } from "../helpers/users";

const BASE_URL = "http://localhost:5199";

test.describe("hms-date: explicit dates always win (ADR-0020 #4)", () => {
  test("/billing/reports with from/to and an untouched period dropdown returns the typed range", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
    // Deliberately NO range=custom — the old defect returned today's figures under these headings.
    await page.goto("/billing/reports?from=01%2F01%2F2026&to=31%2F01%2F2026");
    const hint = page.locator(".card-hint").first();
    await expect(hint).toContainText("01 Jan 2026");
    await expect(hint).toContainText("31 Jan 2026");
    // And the dropdown follows the dates, not the other way round.
    await expect(page.locator("#Range")).toHaveValue("custom");
  });

  test("forgiving formats parse: ISO and dd MMM yyyy give the same range", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
    await page.goto("/billing/reports?from=2026-01-05&to=05%20Feb%202026");
    const hint = page.locator(".card-hint").first();
    await expect(hint).toContainText("05 Jan 2026");
    await expect(hint).toContainText("05 Feb 2026");
  });

  test("no native date input anywhere on reports or masters", async ({ browser }) => {
    for (const [user, path] of [["rasel", "/billing/reports"], ["admin", "/admin/masters"]] as const) {
      const page = await (await browser.newContext({ storageState: authFile(user) })).newPage();
      await page.goto(path);
      expect(await page.locator('input[type="date"]').count(), `${path} must not use type=date`).toBe(0);
    }
  });
});

test.describe("hms-search: predicate reaches the database (ADR-0020 #2)", () => {
  test("/billing/dues finds an unpaid invoice by its invoice number and by patient UHID", async ({ browser }) => {
    // seed-extra leaves part-paid orders behind; find one due via the page itself first.
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    const all = await (await api.get("/billing/dues")).text();
    const invoiceNo = /INV-[A-Z0-9-]+/.exec(all)?.[0];
    test.skip(!invoiceNo, "no open dues in the seed state");
    const searched = await (await api.get(`/billing/dues?Q=${invoiceNo}`)).text();
    expect(searched).toContain(invoiceNo!);
  });

  test("/billing/refund finds an invoice by number through the SQL predicate", async ({ browser }) => {
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    const recent = await (await api.get("/billing/refund")).text();
    const invoiceNo = /INV-[A-Z0-9-]+/.exec(recent)?.[0];
    test.skip(!invoiceNo, "no invoices in the seed state");
    const searched = await (await api.get(`/billing/refund?Q=${invoiceNo}`)).text();
    expect(searched).toContain(invoiceNo!);
  });
});

test.describe("hms-typeahead: one picker product-wide (ADR-0020 #3, §7 U5)", () => {
  test("endpoint: 2-char minimum, ranked hits with value+label", async () => {
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    expect(await (await api.get("/api/typeahead/patients?q=R")).json()).toEqual([]);
    const hits: { value: number; label: string }[] =
      await (await api.get("/api/typeahead/patients?q=Rahim")).json();
    expect(hits.length).toBeGreaterThan(0);
    expect(hits[0].label).toMatch(/Rahim/);
    expect(hits[0].value).toBeGreaterThan(0);
  });

  test("endpoint requires authorization like the directory it fronts (§12/G10)", async () => {
    const anon = await request.newContext({ baseURL: BASE_URL });
    const res = await anon.get("/api/typeahead/patients?q=Rahim", { maxRedirects: 0 });
    expect([302, 401, 403]).toContain(res.status());
  });

  test("OPD, diagnostics order and appointments all use the shared picker — no patient <select> remains", async ({ browser }) => {
    for (const [user, path] of [
      ["rasel", "/billing/opd"], ["rasel", "/diagnostics/order"], ["jashim", "/appointments"],
    ] as const) {
      const page = await (await browser.newContext({ storageState: authFile(user) })).newPage();
      await page.goto(path);
      await expect(page.locator('input[data-typeahead="/api/typeahead/patients"]'),
        `${path} should carry the shared type-ahead`).toHaveCount(1);
      expect(await page.locator('select[name="PatientId"]').count(),
        `${path} must not use a patient <select>`).toBe(0);
    }
  });

  test("typing 3 chars offers ranked results; Enter selects and drives the form", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
    await page.goto("/diagnostics/order");
    const input = page.locator('input[data-typeahead="/api/typeahead/patients"]');
    await input.pressSequentially("Rah", { delay: 60 });
    const results = page.locator(".typeahead-results li");
    await expect(results.first()).toBeVisible();
    await expect(results.first()).toContainText("Rahim");
    await input.press("Enter");                       // data-submit posts the form
    await page.waitForLoadState();
    await expect(page.locator('input[name="PatientId"]')).toHaveValue(/\d+/);
  });
});
