import { chromium, type FullConfig } from "@playwright/test";
import fs from "node:fs";
import path from "node:path";
import { USERS, authFile, AUTH_DIR } from "./helpers/users";
import { seedExtraLisData } from "./helpers/seed-extra";

/**
 * Logs every demo-cast user in once (real POST /login with the antiforgery token, exactly like
 * an operator's browser) and saves a storageState per user. Tests then pick up the right session
 * via `test.use({ storageState: authFile(user) })` instead of re-logging-in per test.
 */
export default async function globalSetup(_config: FullConfig) {
  fs.mkdirSync(AUTH_DIR, { recursive: true });

  const browser = await chromium.launch();
  for (const user of Object.keys(USERS)) {
    const context = await browser.newContext({ baseURL: "http://localhost:5199" });
    const page = await context.newPage();
    await page.goto("/login");
    await page.fill('input[name="Username"]', user);
    await page.fill('input[name="Password"]', USERS[user].password);
    await Promise.all([
      page.waitForURL((url) => !url.pathname.startsWith("/login")),
      page.click('button[type="submit"]'),
    ]);
    await context.storageState({ path: authFile(user) });
    await context.close();
  }
  await browser.close();

  await seedExtraLisData("http://localhost:5199");
}
