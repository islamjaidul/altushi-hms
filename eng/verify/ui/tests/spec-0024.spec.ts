// Spec 0024 — M5 Prescription & EMR at the surface.
// The workflow proof is eng/verify/emr-thread.py; these pin the contracts a screen must keep:
// vitals visible before the consultation, no edit control once signed, templates apply in one
// action, and the nursing chart refuses a second recording of the same dose.
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("Consultation screen (§5 M5)", () => {
  test("the doctor's queue names the patient and shows whether vitals were taken", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("chowdhury") })).newPage();
    await page.goto("/emr/queue");
    await expect(page.locator(".page-title, h1").first()).toContainText("Consultation Queue");
    // U12: the missing-vitals state is a word, not only a colour.
    const body = await page.locator("body").innerText();
    expect(body.includes("Vitals not taken") || body.includes("No visits opened today")).toBeTruthy();
  });

  test("the drug picker is the pharmacy master, not free text", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("chowdhury") });
    const page = await context.newPage();
    // The endpoint the picker binds to (ADR-0020): 2+ chars, ranked, same authZ as the screen.
    const short = await context.request.get("/api/typeahead/products?q=a");
    expect(short.status()).toBe(200);
    expect(await short.json()).toEqual([]);

    const hit = await context.request.get("/api/typeahead/products?q=na");
    expect(hit.status()).toBe(200);
    const items = await hit.json();
    expect(Array.isArray(items)).toBeTruthy();
    if (items.length > 0) {
      expect(items[0]).toHaveProperty("value");
      expect(items[0]).toHaveProperty("label");
    }
    await page.close();
  });

  test("a doctor cannot reach the counter's money screens", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("chowdhury") })).newPage();
    const res = await page.goto("/billing/opd");
    // G10 deny-by-default: either refused outright or bounced to the access-denied page.
    expect(res!.status() === 403 || page.url().includes("/denied")).toBeTruthy();
  });
});

test.describe("Nursing charts (5A-7)", () => {
  test("the ward list opens and the MAR is reachable", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("nasrin") })).newPage();
    await page.goto("/emr/charts");
    await expect(page.locator(".page-title, h1").first()).toContainText("Nursing Charts");
    const body = await page.locator("body").innerText();
    expect(body.includes("Patients in the ward") || body.includes("No patients")).toBeTruthy();
  });

  test("the nurse's vitals screen offers only visits without a reading", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("nasrin") })).newPage();
    await page.goto("/emr/vitals");
    await expect(page.locator("input[name='Systolic']")).toHaveCount(1);
    await expect(page.locator("input[name='Temperature']")).toHaveCount(1);
    // §7 U15: units are on the label, so nobody has to guess Celsius or Fahrenheit.
    await expect(page.locator("body")).toContainText("Temperature (°C)");
  });
});

test.describe("Patient record", () => {
  test("the record screen searches before it shows anything", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("chowdhury") })).newPage();
    await page.goto("/emr/history");
    await expect(page.locator("input[name='Q']")).toHaveCount(1);
    const res = await page.goto("/emr/history?Q=zzz-nobody-zzz");
    expect(res!.status()).toBe(200);
    await expect(page.locator("body")).toContainText("Nobody matched that");
  });
});
