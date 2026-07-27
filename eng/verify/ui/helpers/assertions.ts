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

/**
 * §7 icon-font risk: the self-hosted Material Symbols subset must actually be loaded, or the
 * ligature falls back to literal spelled-out words like "dashboard" in the sidebar.
 *
 * Checks EVERY `.icon` element on the page, not just the first — the font can load fine overall
 * while one specific glyph is simply missing from the trimmed subset (a real regression this
 * suite caught: "outbox" was absent and rendered as giant literal text on /diagnostics/delivery's
 * empty state, while every other icon on that same page was a normal ligature). The threshold
 * scales with each element's own font-size (icon-sm/icon-lg/icon-xl all differ), so it isn't
 * tuned to one size: a real ligature glyph renders roughly square, a spelled-out fallback word is
 * many multiples of the font-size wide.
 */
export async function assertIconFontLoaded(page: Page) {
  await page.evaluate(() => (document as any).fonts.ready);
  const loaded = await page.evaluate(() =>
    (document as any).fonts.check('18px "Material Symbols Outlined"'),
  );
  expect(loaded, 'document.fonts.check(18px "Material Symbols Outlined") should be true').toBe(true);

  const icons = page.locator(".icon");
  const n = await icons.count();
  expect(n, "expected at least one .icon element on the page to check").toBeGreaterThan(0);

  for (let i = 0; i < n; i++) {
    const el = icons.nth(i);
    const { width, fontSize, text } = await el.evaluate((e) => {
      const cs = getComputedStyle(e as HTMLElement);
      // Measure the rendered GLYPH content, not the element's box: several icons
      // (.empty-state .icon, .empty-card .icon) are `display:block; margin:0 auto` for
      // centering, so offsetWidth reports the auto-stretched box width regardless of what's
      // inside it — a real ligature glyph in that context would false-fail on box width alone.
      // A Range over the text node gives the actual inline content's bounding box instead.
      const range = document.createRange();
      range.selectNodeContents(e);
      const rect = range.getBoundingClientRect();
      return {
        width: rect.width,
        fontSize: parseFloat(cs.fontSize),
        text: (e.textContent ?? "").trim(),
      };
    });
    if (width === 0) continue; // not rendering anything (e.g. inside a currently-hidden toast)
    expect(
      width,
      `.icon #${i} ("${text}") rendered ${width}px wide at ${fontSize}px font-size — looks like ` +
        `spelled-out fallback text, not a ligature glyph (glyph missing from the subset?)`,
    ).toBeLessThanOrEqual(fontSize * 2.2);
  }
}
