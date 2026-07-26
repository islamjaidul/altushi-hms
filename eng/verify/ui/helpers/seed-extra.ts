import { request as pwRequest, type APIRequestContext } from "@playwright/test";
import { authFile } from "./users";

const TOKEN_RE = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/;

export async function postForm(
  api: APIRequestContext,
  path: string,
  fields: Record<string, string | number | (string | number)[]>,
  refHtml?: string,
) {
  const html = refHtml ?? (await (await api.get(path)).text());
  const m = TOKEN_RE.exec(html);
  const body = new URLSearchParams();
  for (const [k, v] of Object.entries(fields)) {
    if (Array.isArray(v)) v.forEach((x) => body.append(k, String(x)));
    else body.append(k, String(v));
  }
  if (m) body.append("__RequestVerificationToken", m[1]);
  const res = await api.post(path, {
    headers: { "content-type": "application/x-www-form-urlencoded" },
    data: body.toString(),
  });
  return { res, text: await res.text() };
}

function findCatalogRow(html: string, code: string): { id: string; price: string } {
  const re = new RegExp(
    `<span class="cat-code">${code}</span>[\\s\\S]*?<span class="cat-price">([^<]+)</span>[\\s\\S]*?name="catalogId" value="(\\d+)"`,
  );
  const m = re.exec(html);
  if (!m) throw new Error(`catalog row for ${code} not found in /diagnostics/order`);
  return { price: m[1].replace(/[^\d]/g, ""), id: m[2] };
}

/** Grabs the sampleId from whichever advance-form (Collect/Receive/...) sits inside the
 * board-card for a given "LB-00007"-style order number — scoped by string position so it
 * doesn't pick up an unrelated card's sample. */
function sampleIdNear(html: string, orderNo: string): string {
  const at = html.indexOf(orderNo);
  if (at === -1) throw new Error(`${orderNo} not found on /lis/board`);
  const slice = html.slice(at, at + 1500);
  const m = /name="sampleId" value="(\d+)"/.exec(slice);
  if (!m) throw new Error(`no sampleId near ${orderNo} on /lis/board`);
  return m[1];
}

/**
 * The golden-thread + discount-and-dues seed scripts fully process order #1 (verified and
 * delivered), so by the time this Playwright suite runs, none of /lis/results ("Received"/
 * "Resulted") or /lis/verify ("Resulted") or /diagnostics/delivery ("Verified") has a row to
 * select — several §7 UX assertions (U9's patient-banner, U12's flags/pills) have nothing real to
 * check against. This creates three more orders for the same seeded patient (id 1, Rahim Uddin /
 * ALT-000001), each paid in full over real HTTP (same pattern as eng/verify/golden-thread.py —
 * test setup, not app code):
 *
 *   - XRAY-CH (no sample), left unresulted           -> /lis/results worklist            (U9)
 *   - TSH (Serum sample), resulted out-of-range,
 *     left unverified                                -> /lis/verify worklist, with a
 *                                                          real H/L .flag                 (U9, U12)
 *   - ECG (no sample), resulted + verified            -> /diagnostics/delivery Ready,
 *                                                          "Paid" .pill, deliverable       (U12)
 *
 * An earlier version of this fixture also forced a due > 0 onto a verified order via a direct SQL
 * UPDATE, to exercise /diagnostics/delivery's "held" row rendering (§7 U7(b)). That's gone: the
 * app was fixed so payment is the sole release trigger for the lab pipeline
 * (DiagnosticsRelease.ReleasePaidOrdersAsync) — no order that ever reaches the lab board can carry
 * a due any more, so there's nothing left to fake. U7(b) is now covered live in
 * tests/ux-principles.spec.ts by creating a part-paid order and driving it through Due Collection.
 */
export async function seedExtraLisData(baseURL: string) {
  const bill = await pwRequest.newContext({ baseURL, storageState: authFile("rasel") });

  async function orderAndPay(code: string): Promise<string> {
    const html = await (await bill.get("/diagnostics/order?PatientId=1")).text();
    const { id, price } = findCatalogRow(html, code);
    const { res } = await postForm(bill, "/diagnostics/order?handler=Save", {
      PatientId: 1,
      Items: [id],
      DiscountFlat: 0,
      PaidNow: price,
      Tender: "cash",
    });
    const m = /\/diagnostics\/order\/(\d+)/.exec(res.url());
    if (!m) throw new Error(`order for ${code} was not created — landed on ${res.url()}`);
    return m[1];
  }

  await orderAndPay("XRAY-CH"); // left unresulted
  const orderB = await orderAndPay("TSH"); // Serum sample -> needs collect+receive below
  const orderE = await orderAndPay("ECG"); // will be verified, stays deliverable/unheld
  await bill.dispose();

  // ---- TSH needs its tube collected + received before it reaches "Received" -------------------
  const lab = await pwRequest.newContext({ baseURL, storageState: authFile("ripon") });
  const orderNoB = "LB-" + orderB.padStart(5, "0");
  let board = await (await lab.get("/lis/board")).text();
  const sampleId = sampleIdNear(board, orderNoB);
  await postForm(lab, "/lis/board?handler=Collect", { sampleId }, board);
  board = await (await lab.get("/lis/board")).text();
  await postForm(lab, "/lis/board?handler=Receive", { sampleId }, board);

  // 9.5 µIU/mL is well above the TSH reference range (0.4-4.0) -> flags High, giving /lis/verify
  // a real, visible .flag element for U12 without needing the order to be verified first.
  const resHtmlB = await (await lab.get(`/lis/results?orderId=${orderB}`)).text();
  const valueField = /name="(Values\[[^"]+\])"/.exec(resHtmlB);
  if (!valueField) throw new Error(`no result field found for TSH order ${orderB}`);
  await postForm(lab, "/lis/results", { OrderId: orderB, [valueField[1]]: "9.5" }, resHtmlB);
  // orderB is now "resulted" and stays unverified on purpose — the /lis/verify worklist fixture.

  const resHtmlE = await (await lab.get(`/lis/results?orderId=${orderE}`)).text();
  const ot = /name="Narratives\[(\d+)\]"/.exec(resHtmlE);
  if (!ot) throw new Error(`no narrative field found for order ${orderE} — template may not be narrative-only`);
  await postForm(
    lab,
    "/lis/results",
    { OrderId: orderE, [`Narratives[${ot[1]}]`]: "Sinus rhythm, no acute ST-T changes." },
    resHtmlE,
  );
  await lab.dispose();

  // ---- Pathologist verifies E (not B, so /lis/verify keeps a Resulted-stage row to select) -----
  const path = await pwRequest.newContext({ baseURL, storageState: authFile("farhana") });
  const verHtml = await (await path.get(`/lis/verify?orderId=${orderE}`)).text();
  await postForm(path, "/lis/verify", { OrderId: orderE }, verHtml);
  await path.dispose();
}
