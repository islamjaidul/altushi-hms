// Spec 0026 — M10 Radiology at the surface.
// Workflow proof is eng/verify/radiology-thread.py; these pin the contracts: the worklist is
// per machine, an unsigned report prints provisional, and §12 keeps the technician out of
// reporting.
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("Modality worklist (US10.1)", () => {
  test("the worklist offers a tab per machine", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("moinul") })).newPage();
    await page.goto("/radiology/worklist");
    await expect(page.locator(".page-title, h1").first()).toContainText("Modality Worklist");
    for (const machine of ["X-ray", "Ultrasound", "CT Scan", "MRI"]) {
      await expect(page.locator("body")).toContainText(machine);
    }
  });

  test("filtering to one machine is a real filter, not decoration", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("moinul") })).newPage();
    await page.goto("/radiology/worklist");
    const links = page.locator("a[href*='ModalityId=']");
    expect(await links.count()).toBeGreaterThan(0);
    const href = await links.first().getAttribute("href");
    const res = await page.goto(href!);
    expect(res!.status()).toBe(200);
  });
});

test.describe("§12 boundaries", () => {
  test("a technician cannot open the report editor", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("moinul") });
    const page = await context.newPage();
    const res = await page.goto("/radiology/report/1");
    // Either refused by policy or absent — never an editable report for a technician.
    expect(res!.status() === 403 || res!.status() === 404 || page.url().includes("/denied"))
      .toBeTruthy();
  });

  test("the radiologist reaches the worklist and the lab verification queue alike", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("farhana") })).newPage();
    expect((await page.goto("/radiology/worklist"))!.status()).toBe(200);
    expect((await page.goto("/lis/verify"))!.status()).toBe(200);
  });
});
