import { type Page, expect } from "@playwright/test";

/** Console errors + failed asset requests, captured from the moment this is called. Attach
 * before navigation so nothing from the initial load is missed. */
export function watchPage(page: Page) {
  const consoleErrors: string[] = [];
  const pageErrors: string[] = [];
  const failedRequests: string[] = [];

  page.on("console", (msg) => {
    if (msg.type() === "error") consoleErrors.push(msg.text());
  });
  page.on("pageerror", (err) => pageErrors.push(String(err)));
  page.on("response", (res) => {
    const url = res.url();
    // asset requests only (css/js/font) — API/page responses are asserted separately per test
    if (/\.(css|js|woff2?|ttf|otf)(\?.*)?$/i.test(url) && res.status() >= 400) {
      failedRequests.push(`${res.status()} ${url}`);
    }
  });

  return {
    consoleErrors,
    pageErrors,
    failedRequests,
    assertClean() {
      expect(pageErrors, `uncaught JS exceptions: ${pageErrors.join("; ")}`).toEqual([]);
      expect(consoleErrors, `console.error calls: ${consoleErrors.join("; ")}`).toEqual([]);
      expect(failedRequests, `failed asset requests: ${failedRequests.join("; ")}`).toEqual([]);
    },
  };
}

/** Fails loud if the response is the ASP.NET Core developer exception page rather than app HTML. */
export async function assertNotDevExceptionPage(page: Page) {
  const bodyText = await page.locator("body").innerText().catch(() => "");
  expect(bodyText).not.toContain("An unhandled exception occurred");
  expect(bodyText).not.toContain("HTTP ERROR 500");
  const html = await page.content();
  expect(html).not.toMatch(/class="?exceptionDetail|Microsoft\.AspNetCore\.Diagnostics/i);
}

/** Sidebar, topbar title, and status footer — the authed-shell chrome every protected page shares
 * (05 §1). Pass the expected `.page-title` text when the route is a nav-registry item. */
export async function assertShellRendered(page: Page, expectedTitle?: string) {
  await expect(page.locator(".sidebar")).toBeVisible();
  await expect(page.locator(".topbar")).toBeVisible();
  await expect(page.locator(".status-footer")).toBeVisible();
  if (expectedTitle) {
    await expect(page.locator(".topbar .page-title")).toHaveText(expectedTitle);
  }
}

/** §7 icon-font risk: the self-hosted Material Symbols subset must actually be loaded, or the
 * ligature falls back to literal spelled-out words like "dashboard" in the sidebar. */
export async function assertIconFontLoaded(page: Page) {
  await page.evaluate(() => (document as any).fonts.ready);
  const loaded = await page.evaluate(() =>
    (document as any).fonts.check('18px "Material Symbols Outlined"'),
  );
  expect(loaded, 'document.fonts.check(18px "Material Symbols Outlined") should be true').toBe(true);

  const icon = page.locator(".icon").first();
  await expect(icon).toBeAttached();
  const width = await icon.evaluate((el) => (el as HTMLElement).offsetWidth);
  expect(width, `an .icon element rendered ${width}px wide — looks like spelled-out fallback text, not a ligature glyph`).toBeLessThan(30);
}
