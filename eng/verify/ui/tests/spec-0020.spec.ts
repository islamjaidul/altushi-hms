// Spec 0020 — the three lifecycle defects, as screen behaviour.
// Service-level proof lives in Hms.Integration.Tests/LifecycleGapTests; the end-to-end walk
// in eng/verify/lifecycle-thread.py. Here: what the operator sees at the moment it matters.
//
// The admitted-patient cases provision their own admission over real HTTP (the seed-extra
// pattern) and hand the bed back afterwards, so they assert instead of skipping — the
// lifecycle thread discharges its own patient, so relying on leftover state would make these
// tests silently vanish exactly when they matter.
import { test, expect, request as pwRequest, type APIRequestContext } from "@playwright/test";
import { authFile } from "../helpers/users";
import { postForm } from "../helpers/http";

const BASE = "http://localhost:5199";

async function ctx(user: string): Promise<APIRequestContext> {
  return pwRequest.newContext({ baseURL: BASE, storageState: authFile(user) });
}

/** Registers a patient and admits them to a free general bed. Returns ids for cleanup. */
async function admitFreshPatient(): Promise<{ pid: string; admissionId: string; bedId: string }> {
  const fd = await ctx("jashim");
  const stamp = `${Date.now() % 100000}`.padStart(5, "0");
  const name = `Spec0020 ${stamp}`;
  // Registration's duplicate guard (edge 23) holds the save when a similar record exists —
  // correct behaviour, and each provisioned patient looks like the last one. Acknowledge it
  // exactly as the operator's "continue below" does, rather than dodging the guard.
  await postForm(fd, "/registration/new", {
    FullName: name, Sex: "M", AgeOrDob: "44", Phone: `017${stamp}${stamp[0]}${stamp[1]}${stamp[2]}`,
    PatientType: "general", DuplicatesAcknowledged: "true", action: "save",
  });
  const hits = await (await fd.get(`/api/typeahead/patients?q=${encodeURIComponent(name)}`)).json();
  if (hits.length === 0) throw new Error(`registration of ${name} did not take`);
  const pid = String(hits[0].value);

  const admitHtml = await (await fd.get("/ipd/admit")).text();
  const bedId = /<option value="(\d+)">GW[MF]-\d+/.exec(admitHtml)?.[1];
  if (!bedId) throw new Error("no free general bed to admit into");
  const { res } = await postForm(fd, "/ipd/admit", {
    PatientId: pid, Source: "direct", BedId: bedId,
    ServiceChargePct: "0", ReserveOnly: "false",
  }, admitHtml);
  const admissionId = /\/ipd\/folio\/(\d+)/.exec(res.url())?.[1];
  if (!admissionId) throw new Error(`admission failed: ${res.url()}`);
  await fd.dispose();
  return { pid, admissionId, bedId };
}

/**
 * Closes the admission and frees the bed so repeated runs never exhaust the ward.
 *
 * Absconding is only legal from an in-house state. The discharge-with-dues case above settles
 * its folio first, so on that path the Absconded post is refused and the bed was never handed
 * back — spec 0029's fixture ratchet, in the UI suite this time: a run of the Playwright suite
 * cost the ward a bed permanently. If the admission is past that point, walk the discharge with
 * a stated reason instead, exactly as `_harness.settle_and_discharge` does for the Python
 * threads (§3.2 — a discharge with a due needs a reason, and QA's is as good as anyone's).
 */
async function releaseAdmission(admissionId: string, bedId: string) {
  const fd = await ctx("jashim");
  const folio = await (await fd.get(`/ipd/folio/${admissionId}`)).text();
  await postForm(fd, `/ipd/folio/${admissionId}?handler=Absconded`, { AdmissionId: admissionId }, folio);

  const stillOpen = await (await fd.get(`/ipd/discharge/${admissionId}`)).text();
  if (!stillOpen.includes("Gate pass")) {
    const op = await ctx("rasel");
    for (const handler of ["Initiate", "Clear"]) {
      const page = await (await fd.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(fd, `/ipd/discharge/${admissionId}?handler=${handler}`, {
        AdmissionId: admissionId, ClinicalSummary: "QA teardown — returning the bed to the ward.",
      }, page);
    }
    const page = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
    await postForm(op, `/ipd/discharge/${admissionId}?handler=Discharge`, {
      AdmissionId: admissionId, OutstandingReason: "QA teardown — bed returned to the ward",
    }, page);
    await op.dispose();
  }

  const board = await (await fd.get("/ipd/board")).text();
  await postForm(fd, "/ipd/board?handler=CleaningDone", { BedId: bedId }, board);
  await fd.dispose();
}

test.describe("Finding a patient by phone (gap 1, §7 U5)", () => {
  test("the type-ahead accepts a phone typed as plain digits", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    const candidates: { value: number; label: string }[] =
      await (await page.request.get("/api/typeahead/patients?q=01")).json();
    expect(candidates.length, "a patient with a phone must exist to probe").toBeGreaterThan(0);

    // Take the first candidate whose stored phone is DASHED, rather than simply the first
    // candidate (spec 0032). This test asserts that a dashed stored form is findable by its
    // plain digits; picking row 0 blind conflates that with "every patient in the corpus has a
    // well-formed phone", which is a fact about test data, not about the product. It failed
    // exactly that way once t1's patients became the newest.
    const dashedOf = (label: string) => /(\d[\d-]{6,})/.exec(label)?.[1];
    const probe = candidates.find((c) => dashedOf(c.label)?.includes("-"));
    expect(probe, `no patient with a dashed phone among ${candidates.length} hits — ` +
      `every stored 11-digit number should render dashed (§7 U13)`).toBeDefined();

    const digits = dashedOf(probe!.label)!.replace(/\D/g, "");
    const byDigits = await (await page.request.get(`/api/typeahead/patients?q=${digits}`)).json();
    expect(byDigits.some((h: { value: number }) => h.value === probe!.value)).toBe(true);
  });

  test("the directory search accepts the same digits", async ({ browser }) => {
    const page = await (await browser.newContext({ storageState: authFile("jashim") })).newPage();
    const hits = await (await page.request.get("/api/typeahead/patients?q=01")).json();
    expect(hits.length).toBeGreaterThan(0);
    const digits = (/(\d[\d-]{6,})/.exec(hits[0].label)?.[1] ?? "").replace(/\D/g, "");
    await page.goto(`/registration?Q=${digits}`);
    await expect(page.locator("table.data tbody tr").first()).toBeVisible();
  });
});

test.describe("Discharge with money owed (gap 2, §7 U7)", () => {
  test("the one-click gate pass is absent while a balance stands", async () => {
    const { admissionId, bedId } = await admitFreshPatient();
    try {
      const op = await ctx("rasel");
      // Walk to a settlement that leaves the admission fee + bed day unpaid.
      let html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(op, `/ipd/discharge/${admissionId}?handler=Initiate`,
        { AdmissionId: admissionId, ClinicalSummary: "spec 0020" }, html);
      html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(op, `/ipd/discharge/${admissionId}?handler=Clear`, { AdmissionId: admissionId }, html);
      html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(op, `/ipd/discharge/${admissionId}?handler=Prepare`, { AdmissionId: admissionId }, html);
      html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      await postForm(op, `/ipd/discharge/${admissionId}?handler=Confirm`,
        { AdmissionId: admissionId, DiscountFlat: "0" }, html);

      html = await (await op.get(`/ipd/discharge/${admissionId}`)).text();
      expect(html).toContain("is still owed");                       // the whole position
      expect(html).not.toContain("Discharge — print gate pass");     // §7 U7: absent, not warned
      expect(html).toContain('name="OutstandingReason"');            // the reasoned path exists

      // And the server refuses a crafted reasonless POST, not just the screen.
      const { text } = await postForm(op, `/ipd/discharge/${admissionId}?handler=Discharge`,
        { AdmissionId: admissionId, OutstandingReason: "" }, html);
      expect(text).toContain("still owes");
      await op.dispose();
    } finally {
      await releaseAdmission(admissionId, bedId);
    }
  });
});

test.describe("Outdoor charge for an in-house patient (gap 3)", () => {
  test("pharmacy POS and OPD billing both flag an admitted patient", async ({ browser }) => {
    const { pid, admissionId, bedId } = await admitFreshPatient();
    try {
      const billing = await (await browser.newContext({ storageState: authFile("rasel") })).newPage();
      await billing.goto(`/billing/opd?PatientId=${pid}`);
      await expect(billing.locator(".alert.warn", { hasText: "admitted" })).toBeVisible();
      await expect(billing.locator(`a[href="/ipd/folio/${admissionId}"]`).first()).toBeVisible();

      const pharmacy = await (await browser.newContext({ storageState: authFile("parvin") })).newPage();
      await pharmacy.goto(`/pharmacy/pos?PatientId=${pid}`);
      await expect(pharmacy.locator(".alert.warn", { hasText: "admitted" })).toBeVisible();
      await expect(pharmacy.locator("text=ward indent")).toBeVisible();
    } finally {
      await releaseAdmission(admissionId, bedId);
    }
  });
});
