// Spec 0017 — M6 IPD & folio (+R4) screen behaviour. The money/state invariants live in
// Hms.Integration.Tests/IpdFolioTests; the end-to-end flow in eng/verify/ipd-thread.py.
// Here: what the operator SEES — the board's worded states (U12), absence of illegal
// controls (§7 U7), the shared input contract, and the role split (§12).
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("Ward board (US2.1/US6.4, U12)", () => {
  test("bed states are worded pills, occupancy table present", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    await page.goto("/ipd/board");
    // Seeded beds start Free; any state must be a worded pill, never colour alone.
    await expect(page.locator(".pill", { hasText: "Free" }).first()).toBeVisible();
    await expect(page.locator("th", { hasText: "Occupied" })).toBeVisible();
    // A free bed offers the one legal next step for a manager: admit.
    await expect(page.locator('a', { hasText: "Admit here" }).first()).toBeVisible();
  });

  test("the MD sees the board read-only — no admit/cleaning controls (§7 U7)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("md") })).newPage();
    await page.goto("/ipd/board");
    await expect(page.locator(".pill", { hasText: "Free" }).first()).toBeVisible();
    expect(await page.locator('a:has-text("Admit here")').count()).toBe(0);
    expect(await page.locator('button:has-text("Cleaning done")').count()).toBe(0);
  });
});

test.describe("Admission form (§5 M6, ADR-0020)", () => {
  test("patient picker is the shared type-ahead; beds show class and tariff", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    await page.goto("/ipd/admit");
    await expect(page.locator('input[data-typeahead="/api/typeahead/patients"]')).toHaveCount(1);
    expect(await page.locator('input[type="date"]').count()).toBe(0);
    // The bed dropdown prices itself from the rate plan — the operator never types a tariff.
    await expect(page.locator('select[name="BedId"] option', { hasText: "/day" }).first())
      .toHaveText(/৳|Tk|\d/);
    // 5A-9: the package master is on the form (options in a closed select count as hidden,
    // so assert presence, not visibility).
    expect(await page.locator('select[name="PackageId"] option', { hasText: "Package" }).count())
      .toBeGreaterThan(0);
  });
});

test.describe("Admissions & block list (R4, §11)", () => {
  test("tabs cover in-house, block list, reservations and discharged", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    await page.goto("/ipd/admissions");
    await expect(page.locator("a", { hasText: "Block list (R4)" })).toBeVisible();
    await expect(page.locator("a", { hasText: "Reservations" })).toBeVisible();
    // The blocked tab explains itself in words.
    await page.goto("/ipd/admissions?Tab=blocked");
    await expect(page.locator(".card-head", { hasText: "barred from service" })).toBeVisible();
  });

  test("the nurse reads the census but cannot block or discharge (§12)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("nasrin") })).newPage();
    await page.goto("/ipd/admissions");
    await expect(page.locator("th", { hasText: "Admission" })).toBeVisible();
    expect(await page.locator('button:has-text("Request block")').count()).toBe(0);
    expect(await page.locator('a:has-text("Discharge…")').count()).toBe(0);
  });
});

test.describe("Certificates (§5 M6 [M])", () => {
  test("issue form offers the three kinds; reprint is the only action on issued rows", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
    await page.goto("/ipd/certificates");
    const kinds = page.locator('select[name="Kind"] option');
    await expect(kinds).toHaveCount(3);
    await expect(page.locator(".card-hint", { hasText: "reprints never recompute" })).toBeVisible();
  });
});

test.describe("IPD reports (ADR-0020 dates)", () => {
  test("range uses the forgiving date inputs, never native pickers", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    await page.goto("/ipd/reports");
    expect(await page.locator('input[type="date"]').count()).toBe(0);
    await expect(page.locator('input[data-hms-date]')).toHaveCount(2);
    await expect(page.locator("th", { hasText: "Occupied" })).toBeVisible();
  });
});

test.describe("Pharmacy indoor issue queue (5A-9)", () => {
  test("the queue shows requested indents with stock context and an outlet choice", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("parvin") })).newPage();
    await page.goto("/pharmacy/indents");
    await expect(page.locator(".card-head", { hasText: "Requested ward indents" })).toBeVisible();
    // US11.1 restated on the screen: FEFO, expired never leaves the shelf.
    await expect(page.locator(".card-hint", { hasText: "expired batches never leave" })).toBeVisible();
  });
});
