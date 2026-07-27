// Spec 0018 (M2 Front Desk) + spec 0019 (R3 public displays).
// 0018: the help desk is read-only by construction — it answers, never acts.
// 0019: the two public routes are the ONLY anonymous surface, and they carry no PHI.
import { test, expect } from "@playwright/test";
import { authFile } from "../helpers/users";

test.describe("Front Desk help screen (M2, US2.1/US2.2)", () => {
  test("answers the three questions and is read-only by construction", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    await page.goto("/frontdesk");
    // The three answer blocks: patient enquiry, today's doctors, beds free.
    await expect(page.locator(".card-head", { hasText: "Patient enquiry" })).toBeVisible();
    await expect(page.locator(".card-head", { hasText: "Today's doctors" })).toBeVisible();
    await expect(page.locator(".card-head", { hasText: "Beds free now" })).toBeVisible();
    // Shared type-ahead, no native dates.
    await expect(page.locator('input[data-typeahead="/api/typeahead/patients"]')).toHaveCount(1);
    expect(await page.locator('input[type="date"]').count()).toBe(0);
    // Read-only by construction: no POST form in the screen's own content — the only POST
    // on the page is the shell's logout (AC 1/4).
    expect(await page.locator('main form[method="post" i]').count()).toBe(0);
  });

  test("an admitted patient's enquiry shows the live estimate without posting (US2.2)", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    // The ipd-thread admits (and discharges) 'Ipd Thread Patient'; any patient works for the
    // enquiry shape — resolve one via the type-ahead endpoint.
    const hits = await (await page.request.get("/api/typeahead/patients?q=Rahim")).json();
    test.skip(hits.length === 0, "no seeded patient on this database");
    await page.goto(`/frontdesk?patientId=${hits[0].value}`);
    await expect(page.locator(".card-head", { hasText: "Rahim" })).toBeVisible();
    // Either an admission card with the estimate or the honest "not admitted" line.
    const admitted = await page.locator("text=Net payable now").count();
    if (admitted === 0) {
      await expect(page.locator("text=Not admitted right now")).toBeVisible();
    }
  });
});

test.describe("Public surface (R3, §8 N5)", () => {
  test("queue monitor renders anonymously with meta-refresh and no PHI", async ({ browser }) => {
    const context = await browser.newContext(); // no storageState — anonymous
    const page = await context.newPage();
    const res = await page.goto("/public/queue");
    expect(res!.status()).toBe(200);
    expect(page.url()).toContain("/public/queue");           // not bounced to /login
    await expect(page.locator("th", { hasText: "Now serving" })).toBeVisible();
    const html = await page.content();
    expect(html).toContain('http-equiv="refresh"');
    expect(html).not.toContain("৳");                          // no money on a public screen
  });

  test("report-status answers by order number with masked identity only", async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    await page.goto("/public/report-status");
    expect(page.url()).toContain("/public/report-status");
    // Order 1 exists on any thread-seeded database (golden thread's first order).
    await page.fill('input[name="orderNo"]', "LB-00001");
    await page.click('button[type="submit"]');
    const html = await page.content();
    if (html.includes("No report found")) {
      // Fresh DB without order 1 — probe safety holds: same message as a bogus number.
      await page.goto("/public/report-status?orderNo=LB-99999");
      await expect(page.locator("text=No report found")).toBeVisible();
    } else {
      // Identity is masked: a full seeded name like "Rahim Uddin" must not appear.
      expect(html).not.toContain("Rahim Uddin");
      expect(html).not.toContain("৳");
      await expect(page.locator(".pill").first()).toBeVisible();
    }
    // Probing an absurd number yields the same neutral message (no information leak).
    await page.goto("/public/report-status?orderNo=LB-98765432");
    await expect(page.locator("text=No report found")).toBeVisible();
  });

  test("the anonymous surface is ONLY the two public routes", async ({ browser }) => {
    const context = await browser.newContext();
    const page = await context.newPage();
    for (const path of ["/frontdesk", "/ipd/board", "/billing/opd", "/dashboard"]) {
      await page.goto(path);
      expect(page.url()).toContain("/login");
    }
  });
});
