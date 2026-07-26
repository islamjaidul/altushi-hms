import { test, expect } from "@playwright/test";
import { ROUTES, authFile } from "../helpers/users";
import { watchPage, assertNotDevExceptionPage, assertShellRendered, assertIconFontLoaded } from "../helpers/assertions";

test.describe("anonymous /login", () => {
  test("renders the sign-in form", async ({ page }) => {
    const watcher = watchPage(page);
    const res = await page.goto("/login");
    expect(res?.status()).toBe(200);
    await assertNotDevExceptionPage(page);
    await expect(page).toHaveTitle(/Sign in/);
    await expect(page.locator('input[name="Username"]')).toBeVisible();
    await expect(page.locator('input[name="Password"]')).toBeVisible();
    await expect(page.locator('button[type="submit"]')).toBeVisible();
    watcher.assertClean();
  });
});

for (const route of ROUTES) {
  test.describe(`GET ${route.path} (${route.user})`, () => {
    test.use({ storageState: authFile(route.user) });

    test(`200s, no console/network errors, shell + title correct`, async ({ page }) => {
      const watcher = watchPage(page);
      const res = await page.goto(route.path);

      expect(res?.status(), `HTTP status for ${route.path}`).toBe(200);
      await assertNotDevExceptionPage(page);
      await assertShellRendered(page, route.title ?? undefined);
      await assertIconFontLoaded(page);

      watcher.assertClean();
    });
  });
}
