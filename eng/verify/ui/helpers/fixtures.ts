import { type APIRequestContext } from "@playwright/test";
import { postForm, findCatalogRow, sampleIdNear } from "./http";

/** Registers a patient over real HTTP (as whichever user `api` is authenticated as — needs
 * registration.create) and returns the issued UHID. */
export async function registerPatient(
  api: APIRequestContext,
  fullName: string,
  sex: "M" | "F",
  ageYears: number,
  phone: string,
): Promise<{ uhid: string }> {
  const { text } = await postForm(api, "/registration/new", {
    FullName: fullName,
    Sex: sex,
    AgeOrDob: String(ageYears),
    Phone: phone,
    Area: "Sylhet",
    PatientType: "general",
    action: "save",
    // Always acknowledge: RegistrationService.FindDuplicatesAsync matches on dmetaphone-phonetic
    // name + age±2, which a test fixture can coincidentally trip (e.g. against another QA-created
    // patient of a similar age) — without this the response is the duplicate-warning re-render of
    // the SAME form (still containing OTHER, pre-existing patients' "ALT-" UHIDs in its candidate
    // list), and the naive regex below would silently grab a stale id instead of failing loudly.
    DuplicatesAcknowledged: "true",
  });
  const m = /ALT-\d{6}/.exec(text);
  if (!m) throw new Error(`registration of "${fullName}" did not return a UHID (duplicate warning?): ${text.slice(0, 400)}`);
  return { uhid: m[0] };
}

/** Finds a patient's numeric id through the shared type-ahead endpoint (ADR-0020, §7 U5) —
 * the same source every patient picker uses since spec 0015. */
export async function findPatientId(api: APIRequestContext, fullName: string): Promise<string> {
  const res = await api.get(`/api/typeahead/patients?q=${encodeURIComponent(fullName)}`);
  const hits: { value: number; label: string }[] = await res.json();
  const hit = hits.find((h) => h.label.startsWith(fullName));
  if (!hit) throw new Error(`patient "${fullName}" not found via /api/typeahead/patients`);
  return String(hit.value);
}

/** Orders one test for a patient and pays in full, as whichever user `api` is authenticated as
 * (needs diagnostics.order.create + an open counter session). Returns the new order id. */
export async function orderAndPay(api: APIRequestContext, patientId: string, code: string): Promise<string> {
  const html = await (await api.get(`/diagnostics/order?PatientId=${patientId}`)).text();
  const { id, price } = findCatalogRow(html, code);
  const { res } = await postForm(api, "/diagnostics/order?handler=Save", {
    PatientId: patientId,
    Items: [id],
    DiscountFlat: 0,
    PaidNow: price,
    Tender: "cash",
  });
  const m = /\/diagnostics\/order\/(\d+)/.exec(res.url());
  if (!m) throw new Error(`order for ${code} (patient ${patientId}) was not created — landed on ${res.url()}`);
  return m[1];
}

/** Collects and receives an order's tube at the bench (needs lis.sample.collect), moving it from
 * "Awaiting collection" to "Received at lab" so result entry is possible. */
export async function collectAndReceive(api: APIRequestContext, orderId: string): Promise<void> {
  const orderNo = "LB-" + orderId.padStart(5, "0");
  let board = await (await api.get("/lis/board")).text();
  const sampleId = sampleIdNear(board, orderNo);
  await postForm(api, "/lis/board?handler=Collect", { sampleId }, board);
  board = await (await api.get("/lis/board")).text();
  await postForm(api, "/lis/board?handler=Receive", { sampleId }, board);
}
