// Spec 0025 — M7 Operation Theatre at the surface.
// The workflow proof is eng/verify/ot-thread.py; these pin what a screen must keep: the board
// and register open, the schedule form asks for a team, and §12 keeps money away from the OT.
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("OT board and register", () => {
  test("the board lists theatres for today", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("shaheen") })).newPage();
    await page.goto("/ot/board");
    await expect(page.locator(".page-title, h1").first()).toContainText("OT Board");
    // Seeded theatres exist, so the board is never an empty screen with no explanation.
    await expect(page.locator("body")).toContainText("OT-1");
  });

  test("the register accepts a typed date range", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("shaheen") })).newPage();
    const res = await page.goto("/ot/register?From=1%20Jan%202026&To=31%20Dec%202026");
    expect(res!.status()).toBe(200);
    await expect(page.locator("body")).toContainText("Operation register");
  });
});

test.describe("Scheduling", () => {
  test("the form asks for theatre, window and team in one place (US7.1)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("shaheen") })).newPage();
    await page.goto("/ot/schedule");
    for (const field of ["TheatreId", "OperationServiceId", "FromTime", "Minutes", "SurgeonId"]) {
      await expect(page.locator(`[name='${field}']`)).toHaveCount(1);
    }
  });

  test("a time the operator cannot read is refused with a sentence", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("shaheen") })).newPage();
    await page.goto("/ot/schedule");
    // Every `required` select must be filled or the browser blocks the post and the server-side
    // message never happens — the point of the test is the *server's* sentence.
    for (const field of ["PatientId", "OperationServiceId", "TheatreId", "SurgeonId"]) {
      const first = page.locator(`[name='${field}'] option[value]:not([value=''])`).first();
      const value = await first.getAttribute("value");
      test.skip(value === null, `no ${field} available to schedule against`);
      await page.selectOption(`[name='${field}']`, value!);
    }
    await page.fill("[name='FromTime']", "half past nine");
    // Not `button[type=submit]`: the header's sign-out control is one too, and clicking that
    // logs the test out instead of submitting the form (which is exactly what happened first).
    await Promise.all([
      page.waitForLoadState("networkidle"),
      page.getByRole("button", { name: /Schedule/ }).click(),
    ]);
    await expect(page.locator("body")).toContainText("HH:mm");
  });
});

test.describe("§12 boundaries", () => {
  test("the OT in-charge cannot open a billing counter", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("shaheen") })).newPage();
    const res = await page.goto("/billing/opd");
    expect(res!.status() === 403 || page.url().includes("/denied")).toBeTruthy();
  });
});
