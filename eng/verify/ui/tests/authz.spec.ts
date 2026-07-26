import { test, expect, request } from "@playwright/test";
import { DENIED_PAIRS, authFile } from "../helpers/users";

/**
 * §12 / G10: deny by default, server-side. src/Hms.Web/Program.cs sets
 * CookieAuthenticationOptions.AccessDeniedPath = "/denied", so a user who lacks the page's
 * [Authorize(Policy = ...)] permission must land on /denied with a plain-English explanation —
 * never a 404, and never the protected page's own content.
 *
 * Pairs mix different demo users, so each test opens its own browser context seeded from that
 * user's storageState (saved once in global-setup.ts) rather than sharing the top-level `page`.
 */
for (const { path, user, permission } of DENIED_PAIRS) {
  test(`${user} lacks ${permission} -> ${path} redirects to /denied`, async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile(user) });
    const page = await context.newPage();

    const res = await page.goto(path);

    // Not a 404: the AccessDeniedPath redirect lands on a real 200 page.
    expect(res?.status()).toBe(200);
    await expect(page).toHaveURL(/\/denied(\?|$)/);
    await expect(page.locator("body")).toContainText("not part of your role");

    // Never the protected page's own content — the shell's page-title must read "Not allowed".
    await expect(page.locator(".topbar .page-title")).toHaveText("Not allowed");

    await context.close();
  });
}

/**
 * Handler-level check, not page-level: /lis/board is [Authorize(Policy = LisWorklistRead)] only
 * — both lab roles can read the board — but BoardModel.OnPostCollectAsync additionally requires
 * lis.sample.collect and calls Forbid() when it's missing (Pages/Lis/Board.cshtml.cs). A
 * pathologist holds worklist-read but not sample-collect, so a POST straight to the handler
 * (bypassing the UI, which already hides the button — see ux-principles.spec.ts U7(c)) must still
 * be refused server-side, landing on the same /denied page as a page-level authorization failure.
 */
test("farhana lacks lis.sample.collect -> POST /lis/board?handler=Collect is refused server-side, not executed", async () => {
  const api = await request.newContext({ baseURL: "http://localhost:5199", storageState: authFile("farhana") });
  const html = await (await api.get("/lis/board")).text();
  const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(html)?.[1];

  const body = new URLSearchParams({ sampleId: "1" });
  if (token) body.append("__RequestVerificationToken", token);
  const res = await api.post("/lis/board?handler=Collect", {
    headers: { "content-type": "application/x-www-form-urlencoded" },
    data: body.toString(),
  });

  expect(res.url()).toMatch(/\/denied(\?|$)/);
  const text = await res.text();
  expect(text).toContain("not part of your role");

  await api.dispose();
});
