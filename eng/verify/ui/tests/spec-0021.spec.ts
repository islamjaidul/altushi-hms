// Spec 0021 — UI-level proof of the two edge-case fixes.
// The money guarantees live in Hms.Integration.Tests/TerminalExitTests (including the
// concurrent double-post); the end-to-end walk in eng/verify/lifecycle-thread.py.
// Here: what the operator sees, and that the forms carry what makes it safe.
import { test, expect, request as pwRequest, type APIRequestContext } from "@playwright/test";
import { authFile } from "../helpers/users";
import { postForm } from "../helpers/http";

const BASE = "http://localhost:5199";
const ctx = (user: string): Promise<APIRequestContext> =>
  pwRequest.newContext({ baseURL: BASE, storageState: authFile(user) });

async function admitFresh(prefix: string) {
  const fd = await ctx("jashim");
  const stamp = `${Date.now() % 100000}`.padStart(5, "0");
  const name = `${prefix} ${stamp}`;
  await postForm(fd, "/registration/new", {
    FullName: name, Sex: "M", AgeOrDob: "60", Phone: `013${stamp}${stamp.slice(0, 3)}`,
    PatientType: "general", DuplicatesAcknowledged: "true", action: "save",
  });
  const hits = await (await fd.get(`/api/typeahead/patients?q=${encodeURIComponent(name)}`)).json();
  if (hits.length === 0) throw new Error(`registration of ${name} did not take`);
  const pid = String(hits[0].value);

  const admitHtml = await (await fd.get("/ipd/admit")).text();
  const bedId = /<option value="(\d+)">GW[MF]-\d+/.exec(admitHtml)?.[1];
  if (!bedId) throw new Error("no free general bed");
  const { res } = await postForm(fd, "/ipd/admit", {
    PatientId: pid, Source: "direct", BedId: bedId, ServiceChargePct: "0", ReserveOnly: "false",
  }, admitHtml);
  const admissionId = /\/ipd\/folio\/(\d+)/.exec(res.url())?.[1];
  if (!admissionId) throw new Error("admission failed");
  await fd.dispose();
  return { name, pid, admissionId, bedId };
}

async function freeBed(bedId: string) {
  const fd = await ctx("jashim");
  const board = await (await fd.get("/ipd/board")).text();
  await postForm(fd, "/ipd/board?handler=CleaningDone", { BedId: bedId }, board);
  await fd.dispose();
}

test.describe("One submission, one invoice (gap: double-billing)", () => {
  test("every screen that issues a financial document carries a submission token", async ({ browser }) => {
    const billing = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
    for (const path of ["/billing/opd", "/diagnostics/order"]) {
      await billing.goto(path);
      await expect(billing.locator('input[name="SubmissionToken"]'),
        `${path} must carry a submission token`).toHaveCount(1);
      const value = await billing.locator('input[name="SubmissionToken"]').inputValue();
      expect(value, `${path} token must be a real guid`).toMatch(/^[0-9a-f-]{36}$/i);
    }
    const pharmacy = await (await browser.newContext({ storageState: authFile("parvin") })).newPage();
    await pharmacy.goto("/pharmacy/pos");
    await expect(pharmacy.locator('input[name="SubmissionToken"]')).toHaveCount(1);
  });

  test("two visits to the counter mint different tokens (a deliberate second bill still works)",
    async ({ browser }) => {
      const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
      await page.goto("/billing/opd");
      const first = await page.locator('input[name="SubmissionToken"]').inputValue();
      await page.goto("/billing/opd");
      const second = await page.locator('input[name="SubmissionToken"]').inputValue();
      expect(first).not.toBe(second);
    });

  test("a re-posted bill lands on the same invoice, not a second one", async () => {
    const op = await ctx("rasel");
    // Open a counter if this operator has none.
    const sess = await (await op.get("/billing/session")).text();
    if (!sess.includes("Your counter is open")) {
      const counterId = /value="(\d+)"[^>]*>[^<]*Front Desk/.exec(sess)?.[1] ?? "1";
      await postForm(op, "/billing/session", { CounterId: counterId, OpeningFloat: "2000" }, sess);
    }
    const fd = await ctx("jashim");
    const hits = await (await fd.get("/api/typeahead/patients?q=01")).json();
    test.skip(hits.length === 0, "no patient to bill");
    const pid = String(hits[0].value);

    let html = await (await op.get(`/billing/opd?PatientId=${pid}`)).text();
    const token = /name="SubmissionToken" value="([0-9a-f-]{36})"/i.exec(html)![1];
    const catalogId = /name="catalogId" value="(\d+)"/.exec(html)![1];
    ({ text: html } = await postForm(op, "/billing/opd?handler=Add",
      { catalogId, PatientId: pid, SubmissionToken: token }, html));
    const gross = /data-gross="(\d+)"/.exec(html)?.[1] ?? "0";

    const fields = { PatientId: pid, Items: [catalogId], SubmissionToken: token,
                     PaidNow: gross, Tender: "cash" };
    const a = await postForm(op, "/billing/opd?handler=Save", { ...fields }, html);
    const b = await postForm(op, "/billing/opd?handler=Save", { ...fields }, html);
    expect(a.res.url()).toContain("/billing/invoice/");
    expect(b.res.url()).toBe(a.res.url());          // the double-click is harmless
    await op.dispose();
    await fd.dispose();
  });
});

test.describe("Terminal exits can still be billed (gap: stranded money)", () => {
  test("an absconded patient's bill can be closed and becomes a normal due", async ({ browser }) => {
    const { name, admissionId, bedId } = await admitFresh("Spec0021 Abscond");
    try {
      const fd = await ctx("jashim");
      const op = await ctx("rasel");
      await (await op.get(`/ipd/folio/${admissionId}`)).text();      // catch up the bed day
      const folio = await (await fd.get(`/ipd/folio/${admissionId}`)).text();
      await postForm(fd, `/ipd/folio/${admissionId}?handler=Absconded`,
        { AdmissionId: admissionId }, folio);

      const page = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
      await page.goto(`/ipd/discharge/${admissionId}`);
      // The wording must never claim a discharge for someone who walked out.
      await expect(page.locator(".card-head", { hasText: "Close the bill" })).toBeVisible();
      expect(await page.locator('button:has-text("Discharge — print gate pass")').count()).toBe(0);

      let html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(op, `/ipd/discharge/${admissionId}?handler=Prepare`,
        { AdmissionId: admissionId }, html);
      html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      const token = /name="SubmissionToken" value="([0-9a-f-]{36})"/i.exec(html)?.[1] ?? "";
      const { res } = await postForm(op, `/ipd/discharge/${admissionId}?handler=Confirm`,
        { AdmissionId: admissionId, DiscountFlat: "0", SubmissionToken: token }, html);
      expect(res.url()).toContain("/billing/invoice/");
      const invoiceId = res.url().split("/").pop()!;

      const dues = await (await op.get(`/billing/dues?Q=${encodeURIComponent(name)}`)).text();
      expect(dues, "§11's 'due follow-up' needs an actual due")
        .toContain(`name="InvoiceId" value="${invoiceId}"`);

      // The clinical fact stands: still Absconded, never "discharged".
      await page.goto("/ipd/admissions?Tab=closed");
      await expect(page.locator(".pill", { hasText: "Absconded" }).first()).toBeVisible();
      await fd.dispose();
      await op.dispose();
    } finally {
      await freeBed(bedId);
    }
  });
});
