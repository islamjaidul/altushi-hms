import { test, expect, request } from "@playwright/test";
import { authFile } from "../helpers/users";
import { postForm, findCatalogRow } from "../helpers/http";
import { registerPatient, findPatientId, orderAndPay, collectAndReceive } from "../helpers/fixtures";

const BASE_URL = "http://localhost:5199";

function rand(n = 6): string {
  return Array.from({ length: n }, () => String.fromCharCode(65 + Math.floor(Math.random() * 26))).join("");
}

// =====================================================================================
// 1. Refund / cancel (§11, 05 §5 screen 7)
// =====================================================================================
test.describe("Refund / cancel workflow", () => {
  test("raise -> escalates -> supervisor approves -> operator carries out -> Refunded; a second reversal is refused server-side", async ({ browser }) => {
    // ---- create + pay a fresh OPD invoice for patient 1 (Rahim Uddin) ----
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    const opdHtml = await (await api.get("/billing/opd?PatientId=1")).text();
    const { id: svcId, price } = findCatalogRow(opdHtml, "CON-SPC");
    const { res, text: invoiceHtml } = await postForm(api, "/billing/opd?handler=Save", {
      PatientId: 1,
      Items: [svcId],
      DiscountFlat: 0,
      PaidNow: price,
      Tender: "cash",
    });
    const invoiceId = /\/billing\/invoice\/(\d+)/.exec(res.url())?.[1];
    if (!invoiceId) throw new Error(`invoice was not created — landed on ${res.url()}`);
    const invoiceNo = /<div class="sheet-doc-no">([^\s<]+)/.exec(invoiceHtml)?.[1];
    if (!invoiceNo) throw new Error("could not read the new invoice's InvoiceNo off its receipt page");
    await api.dispose();

    const reason = `QA refund reason ${rand()}`;

    // ---- rasel raises a refund for the full paid amount ----
    const raselCtx = await browser.newContext({ storageState: authFile("rasel") });
    const raselPage = await raselCtx.newPage();
    raselPage.on("dialog", (d) => d.accept());
    await raselPage.goto("/billing/refund");
    await raselPage.locator(`a[onclick*="hmsPickInvoice(${invoiceId},"]`).click();
    await expect(raselPage.locator("#InvoiceId")).toHaveValue(invoiceId);
    await expect(raselPage.locator("#Action")).toHaveValue("refund"); // paid > 0 -> refund, not cancel
    await raselPage.locator("#Amount").fill(price);
    await raselPage.locator('textarea[name="Reason"]').fill(reason);
    await raselPage.getByRole("button", { name: "Send for approval" }).click();
    // The refund policy has no Tier-0 for the Billing Operator (unlike discount) — every refund
    // escalates, so this always lands in the supervisor's inbox rather than auto-approving.
    await expect(raselPage.locator("body")).toContainText("supervisor's approvals inbox", { timeout: 10_000 });
    await raselCtx.close();

    // ---- shahid (Billing Supervisor) approves it ----
    const shahidCtx = await browser.newContext({ storageState: authFile("shahid") });
    const shahidPage = await shahidCtx.newPage();
    await shahidPage.goto("/admin/approvals");
    const card = shahidPage.locator(".approval-card", { hasText: reason });
    await expect(card, `no pending approval found with reason "${reason}"`).toBeVisible();
    await card.getByRole("button", { name: "Approve", exact: true }).click();
    await expect(shahidPage.locator("body")).not.toContainText(reason, { timeout: 10_000 });
    await shahidCtx.close();

    // ---- rasel carries out the now-approved refund ----
    const carryCtx = await browser.newContext({ storageState: authFile("rasel") });
    const carryPage = await carryCtx.newPage();
    carryPage.on("dialog", (d) => d.accept());
    await carryPage.goto("/billing/refund");
    const approvedRow = carryPage.locator("tr", { hasText: reason });
    await expect(approvedRow, "refund not found in the 'Approved — waiting for you to carry out' list").toBeVisible();
    await Promise.all([
      carryPage.waitForURL(new RegExp(`/billing/invoice/${invoiceId}$`)),
      approvedRow.getByRole("button", { name: "Carry out", exact: true }).click(),
    ]);
    await carryCtx.close();

    // ---- the invoice now shows a Refunded pill on /billing/refund's own candidate list — a
    // permanent state, not a transient toast — with no Select control offered any more.
    const verifyCtx = await browser.newContext({ storageState: authFile("rasel") });
    const verifyPage = await verifyCtx.newPage();
    await verifyPage.goto("/billing/refund");
    const invoiceRow = verifyPage.locator("tr", { hasText: invoiceNo });
    await expect(invoiceRow, `row for ${invoiceNo} not found on /billing/refund`).toBeVisible();
    await expect(invoiceRow.locator(".pill")).toHaveText("Refunded");
    await expect(invoiceRow).toContainText("reversed");
    await expect(invoiceRow.getByRole("link", { name: "Select", exact: true })).toHaveCount(0);
    await verifyCtx.close();

    // ---- a second reversal is refused server-side, not merely hidden in the UI ----
    const api2 = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    const { text } = await postForm(api2, "/billing/refund?handler=Request", {
      InvoiceId: invoiceId,
      Action: "refund",
      Amount: 1,
      Reason: "second reversal attempt — must be refused",
    });
    expect(text).toContain("it cannot be reversed twice");
    await api2.dispose();
  });
});

// =====================================================================================
// 2. Split multi-tender + discount reason (5A-4, §5 M4 [M])
// =====================================================================================
test.describe("Split multi-tender + mandatory discount reason", () => {
  test.use({ storageState: authFile("rasel") });

  test("a discount with no reason is refused; with a reason + split tender it saves two receipts, and both show on day-close", async ({ page }) => {
    await page.goto("/billing/opd?PatientId=1");
    await Promise.all([
      page.waitForResponse((r) => r.url().includes("/billing/opd") && r.request().method() === "POST"),
      page.getByRole("button", { name: "Add Doctor Consultation (General)", exact: true }).click(),
    ]);

    await page.locator("#DiscountFlat").fill("100"); // net = 700 - 100 = 600
    await page.getByRole("button", { name: /save & print money receipt/i }).click();
    await expect(page.locator(".alert.bad")).toContainText("A discount needs a reason", { timeout: 10_000 });

    const reason = `QA split-tender discount ${rand()}`;
    await page.locator("#DiscountReason").fill(reason);
    await page.locator("[data-paid]").fill("300");
    await page.locator("#split-toggle").click();
    await page.locator("[data-paid2]").fill("300");
    await page.locator('select[name="Tender2"]').selectOption("bkash");

    await Promise.all([
      page.waitForURL(/\/billing\/invoice\/\d+/),
      page.getByRole("button", { name: /save & print money receipt/i }).click(),
    ]);
    const invoiceId = /\/billing\/invoice\/(\d+)/.exec(page.url())![1];

    // Two receipts on the one invoice.
    const receiptCount = (await page.locator(".sheet").innerText()).match(/RCP-/g)?.length ?? 0;
    expect(receiptCount, "expected two RCP- receipt lines on the invoice").toBe(2);
    await expect(page.locator(".sheet")).toContainText("Cash");
    await expect(page.locator(".sheet")).toContainText("Bkash");

    // The reason lands on the MD dashboard's discount table.
    const mdCtx = await page.context().browser()!.newContext({ storageState: authFile("md") });
    const mdPage = await mdCtx.newPage();
    await mdPage.goto("/dashboard");
    await expect(mdPage.locator("body")).toContainText(reason);
    await mdCtx.close();

    // ...and in /billing/reports' discount register (default range = today).
    await page.goto("/billing/reports");
    await expect(page.locator("body")).toContainText(reason);

    // Both tender modes show on day-close for the still-open session.
    await page.goto("/billing/day-close");
    await expect(page.locator("body")).toContainText("Cash");
    await expect(page.locator("body")).toContainText("Bkash");

    void invoiceId;
  });

  test("over-tendering (paying more than payable) is refused", async ({ page }) => {
    await page.goto("/billing/opd?PatientId=1");
    await Promise.all([
      page.waitForResponse((r) => r.url().includes("/billing/opd") && r.request().method() === "POST"),
      page.getByRole("button", { name: "Add Dressing (Minor)", exact: true }).click(), // ৳300, no discount
    ]);
    await page.locator("[data-paid]").fill("300");
    await page.locator("#split-toggle").click();
    await page.locator("[data-paid2]").fill("500"); // 300 + 500 = 800 > 300 payable
    await page.getByRole("button", { name: /save & print money receipt/i }).click();
    await expect(page.locator(".alert.bad")).toContainText("adds up to more than", { timeout: 10_000 });
  });
});

// =====================================================================================
// 3. Bulk import (§9A.1 F1)
// =====================================================================================
test.describe("Bulk import", () => {
  test.use({ storageState: authFile("admin") });

  test("2 good rows import, the bad row is listed per-row with its column, and the template downloads as CSV", async ({ page }) => {
    const codeA = "QAI" + rand(5);
    const codeB = "QAI" + rand(5);
    const codeBad = "QAI" + rand(5);
    const today = new Date().toISOString().slice(0, 10);
    const csv =
      "code,name,dept,sample_types,tat_minutes,price,valid_from\n" +
      `${codeA},QA Import Test A,Biochemistry,Serum,60,300,${today}\n` +
      `${codeB},QA Import Test B,Hematology,EDTA,90,450,${today}\n` +
      `${codeBad},QA Import Test Bad,Biochemistry,Serum,60,NOT_A_PRICE,${today}\n`;

    await page.goto("/admin/import");
    await page.locator('textarea[name="Csv"]').fill(csv);
    await Promise.all([
      page.waitForResponse((r) => r.url().includes("/admin/import") && r.request().method() === "POST"),
      page.getByRole("button", { name: "Import catalog", exact: true }).click(),
    ]);

    await expect(page.locator(".alert.good")).toContainText("2 added");

    // The bad row comes back per-row with its column and what to fix — not a whole-file rejection.
    const errorRow = page.locator("tbody tr", { hasText: "NOT_A_PRICE" });
    await expect(errorRow, "expected the bad row (invalid price) listed under 'Rows that need attention'").toBeVisible();
    await expect(errorRow.locator(".pill.warn")).toHaveText("price");

    // The imported test is visible on the price list.
    await page.goto("/admin/masters");
    await expect(page.locator("body")).toContainText(codeA);
    await expect(page.locator("body")).toContainText(codeB);
    await expect(page.locator("body")).not.toContainText(codeBad);

    // Template download is a real CSV.
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("admin") });
    const tmplRes = await api.get("/admin/import?handler=Template");
    expect(tmplRes.status()).toBe(200);
    expect(tmplRes.headers()["content-type"] ?? "").toContain("text/csv");
    const tmplText = await tmplRes.text();
    expect(tmplText.startsWith("code,name,dept,sample_types,tat_minutes,price,valid_from")).toBe(true);
    await api.dispose();
  });
});

// =====================================================================================
// 4. Effective-dated pricing (US21.1)
// =====================================================================================
test.describe("Effective-dated pricing", () => {
  test.use({ storageState: authFile("admin") });

  test("a future-dated price change keeps both versions with authors; back-dating is refused server-side", async ({ page }) => {
    const code = "QAP" + rand(5);
    await page.goto("/admin/masters");
    await page.locator("#Kind").selectOption("test");
    await page.locator("#Code").fill(code);
    await page.locator("#Name").fill(`QA Price Item ${code}`);
    await page.locator("#Dept").fill("Biochemistry");
    await page.locator("#SampleType").fill("Serum");
    await page.locator("#TatMinutes").fill("60");
    await page.locator("#Price").fill("500");
    await Promise.all([
      page.waitForURL(/\/admin\/masters$/),
      page.getByRole("button", { name: "Add to catalog", exact: true }).click(),
    ]);

    const row = page.locator("tr", { hasText: code });
    await expect(row).toBeVisible();
    const targetId = await row.locator('input[name="TargetId"]').getAttribute("value");
    if (!targetId) throw new Error(`could not read TargetId for ${code}`);

    const future = new Date(Date.now() + 30 * 86_400_000).toISOString().slice(0, 10);
    await row.locator('input[name="NewPrice"]').fill("650");
    await row.locator('input[name="EffectiveFrom"]').fill(future);
    await Promise.all([
      page.waitForURL(/history=/),
      row.getByRole("button", { name: "Apply", exact: true }).click(),
    ]);

    const historyRows = page.locator(".card:has-text('Price history') table.data tbody tr");
    await expect(historyRows).toHaveCount(2);
    await expect(page.locator(".card:has-text('Price history')")).toContainText("500");
    await expect(page.locator(".card:has-text('Price history')")).toContainText("650");
    await expect(page.locator(".card:has-text('Price history')")).toContainText("System Admin");

    // Back-dating is refused server-side (not just a disabled UI control).
    const api = await request.newContext({ baseURL: BASE_URL, storageState: authFile("admin") });
    const yesterday = new Date(Date.now() - 86_400_000).toISOString().slice(0, 10);
    const mastersHtml = await (await api.get("/admin/masters")).text();
    const { text } = await postForm(api, "/admin/masters?handler=Reprice", {
      TargetId: targetId,
      TargetKind: "test",
      NewPrice: 999,
      EffectiveFrom: yesterday,
    }, mastersHtml);
    expect(text).toContain("cannot start in the past");
    await api.dispose();
  });
});

// =====================================================================================
// 5. Age/sex reference bands (§5 M9 [M]) + live H/L flag column (client-side, no round trip)
// =====================================================================================
test.describe("Age/sex reference bands + live flag", () => {
  let maleOrderId = "";
  let femaleOrderId = "";

  test.beforeAll(async () => {
    const jashimApi = await request.newContext({ baseURL: BASE_URL, storageState: authFile("jashim") });
    const maleName = `QA Male ${rand(8)}`;
    const femaleName = `QA Female ${rand(8)}`;
    await registerPatient(jashimApi, maleName, "M", 46, "017" + Math.floor(10_000_000 + Math.random() * 89_999_999));
    await registerPatient(jashimApi, femaleName, "F", 39, "017" + Math.floor(10_000_000 + Math.random() * 89_999_999));
    await jashimApi.dispose();

    const raselApi = await request.newContext({ baseURL: BASE_URL, storageState: authFile("rasel") });
    const maleId = await findPatientId(raselApi, maleName);
    const femaleId = await findPatientId(raselApi, femaleName);
    maleOrderId = await orderAndPay(raselApi, maleId, "CBC");
    femaleOrderId = await orderAndPay(raselApi, femaleId, "CBC");
    await raselApi.dispose();

    const riponApi = await request.newContext({ baseURL: BASE_URL, storageState: authFile("ripon") });
    await collectAndReceive(riponApi, maleOrderId);
    await collectAndReceive(riponApi, femaleOrderId);
    await riponApi.dispose();
  });

  test.use({ storageState: authFile("ripon") });

  test("male vs female HB and HCT bands differ, read from data-low/data-high (encoding-proof)", async ({ page }) => {
    await page.goto(`/lis/results?orderId=${maleOrderId}`);
    const maleHb = page.locator("tr", { hasText: "Haemoglobin" }).locator("input");
    const maleHct = page.locator("tr", { hasText: "Haematocrit" }).locator("input");
    await expect(maleHb).toHaveAttribute("data-low", "13.0");
    await expect(maleHb).toHaveAttribute("data-high", "17.0");
    await expect(maleHct).toHaveAttribute("data-low", "40");
    await expect(maleHct).toHaveAttribute("data-high", "52");

    await page.goto(`/lis/results?orderId=${femaleOrderId}`);
    const femaleHb = page.locator("tr", { hasText: "Haemoglobin" }).locator("input");
    const femaleHct = page.locator("tr", { hasText: "Haematocrit" }).locator("input");
    await expect(femaleHb).toHaveAttribute("data-low", "12.0");
    await expect(femaleHb).toHaveAttribute("data-high", "15.0");
    await expect(femaleHct).toHaveAttribute("data-low", "36");
    await expect(femaleHct).toHaveAttribute("data-high", "46");
  });

  test("typing a value below the band's low turns the flag cell to Low without a round trip", async ({ page }) => {
    await page.goto(`/lis/results?orderId=${maleOrderId}`);
    const row = page.locator("tr", { hasText: "Haemoglobin" });
    const input = row.locator("input");
    const flag = row.locator(".flag");

    await expect(flag).toHaveText("—");
    const watcher: string[] = [];
    page.on("request", (r) => {
      if (r.method() === "POST") watcher.push(r.url());
    });
    await input.fill("10"); // male HB low is 13.0
    await expect(flag).toHaveText("Low");
    await expect(flag).toHaveClass("flag low");
    expect(watcher, "the flag update must be client-side only — no POST should have fired").toEqual([]);
  });
});

// =====================================================================================
// 6. Thermal receipt (§5 M4 [M])
// =====================================================================================
test.describe("Thermal receipt paper size", () => {
  test.use({ storageState: authFile("rasel") });

  test("?paper=thermal renders .sheet.thermal; the default is plain (A4)", async ({ page }) => {
    await page.goto("/billing/invoice/1");
    await expect(page.locator(".sheet")).not.toHaveClass(/thermal/);

    await page.goto("/billing/invoice/1?paper=thermal");
    await expect(page.locator(".sheet")).toHaveClass(/thermal/);
  });
});
