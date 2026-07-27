import { type APIRequestContext } from "@playwright/test";

/**
 * Real HTTP form-post + antiforgery-token helpers, shared by helpers/seed-extra.ts and the
 * spec-0013 tests. Same pattern as eng/verify/golden-thread.py — this is test setup / test
 * assertion plumbing, not app code, and it drives the real HTTP surface a browser form-post uses.
 */

export const TOKEN_RE = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/;

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

/** A `/diagnostics/order` or `/billing/opd` catalogue row: `<span class="cat-code">CODE</span>
 * ... <span class="cat-price">&#x9f3; N,NNN</span> ... name="catalogId" value="ID"`.
 *
 * Ui.Money renders the taka sign as the HTML entity `&#x9f3;` — a naive "strip everything that
 * isn't a digit" on the raw markup also eats the `9` and `3` inside `&#x9f3;` itself, turning
 * "1,200" into "931200". Capture only the digit/comma run that follows the entity instead.
 */
export function findCatalogRow(html: string, code: string): { id: string; price: string } {
  const re = new RegExp(
    `<span class="cat-code">${code}</span>[\\s\\S]*?<span class="cat-price">(?:&#x9f3;|৳)?\\s*([\\d,]+)</span>[\\s\\S]*?name="catalogId" value="(\\d+)"`,
    "i",
  );
  const m = re.exec(html);
  if (!m) throw new Error(`catalog row for ${code} not found`);
  return { price: m[1].replace(/,/g, ""), id: m[2] };
}

/** Grabs the sampleId from whichever advance-form (Collect/Receive/...) sits inside the
 * board-card for a given "LB-00007"-style order number — scoped by string position so it
 * doesn't pick up an unrelated card's sample. */
export function sampleIdNear(html: string, orderNo: string): string {
  const at = html.indexOf(orderNo);
  if (at === -1) throw new Error(`${orderNo} not found on /lis/board`);
  const slice = html.slice(at, at + 1500);
  const m = /name="sampleId" value="(\d+)"/.exec(slice);
  if (!m) throw new Error(`no sampleId near ${orderNo} on /lis/board`);
  return m[1];
}

/** Grabs a named hidden/value attribute from within a bounded window after the first occurrence
 * of `anchorText` — for scraping a specific row's per-row form field (e.g. a Reprice row's
 * TargetId) without needing a full HTML parser. */
export function fieldNear(html: string, anchorText: string, fieldName: string, window = 1200): string {
  const at = html.indexOf(anchorText);
  if (at === -1) throw new Error(`"${anchorText}" not found in page`);
  const slice = html.slice(at, at + window);
  const m = new RegExp(`name="${fieldName}" value="([^"]*)"`).exec(slice);
  if (!m) throw new Error(`no ${fieldName} found within ${window} chars after "${anchorText}"`);
  return m[1];
}
