import { test, expect } from "@playwright/test";
import { watchPage, assertNotDevExceptionPage } from "../helpers/assertions";

/**
 * Spec 0040. These belong in a real browser and nowhere else: the defect was invisible to every
 * other layer we have. `LoginModel.Validate` was correct and unit-tested, and an HTTP POST with
 * empty fields returned the page carrying both messages — so the C# tests passed, the verify
 * scripts passed, and the operator still saw nothing.
 *
 * The cause was `required`: the browser refused to submit the form at all, so neither tier of
 * our validation was ever reached and the only feedback was the browser's own bubble — its
 * wording, its language, gone by the next keystroke. Only a browser can see that difference.
 */
test.describe("spec 0040 — login validation the operator can actually see", () => {
  test("an empty submit shows OUR message under each box", async ({ page }) => {
    const watcher = watchPage(page);
    await page.goto("/login");
    await page.click('button[type="submit"]');

    // The product's words, in the product's language, in the product's error style.
    const username = page.locator('[data-error-for="Username"]');
    const password = page.locator('[data-error-for="Password"]');
    await expect(username).toBeVisible();
    await expect(username).toHaveText("Enter your username.");
    await expect(password).toBeVisible();
    await expect(password).toHaveText("Enter your password.");

    // Both boxes are marked wrong, for sighted and assistive users alike.
    await expect(page.locator('input[name="Username"]')).toHaveClass(/input-invalid/);
    await expect(page.locator('input[name="Username"]')).toHaveAttribute("aria-invalid", "true");
    await expect(page.locator('input[name="Password"]')).toHaveAttribute("aria-invalid", "true");

    await assertNotDevExceptionPage(page);
    watcher.assertClean();
  });

  test("only the empty box is named, and the message clears when it is filled", async ({ page }) => {
    await page.goto("/login");
    await page.fill('input[name="Username"]', "farid");
    await page.click('button[type="submit"]');

    // A username was typed, so only the password is complained about — the operator is not
    // sent back to a field they already got right (§7: slow typists, keyboard-driven).
    await expect(page.locator('[data-error-for="Username"]')).toBeHidden();
    await expect(page.locator('[data-error-for="Password"]')).toHaveText("Enter your password.");

    // Typing into the offending box withdraws the complaint immediately: leaving the message
    // under a filled box reads as a second, unexplained failure.
    await page.fill('input[name="Password"]', "x");
    await expect(page.locator('[data-error-for="Password"]')).toBeHidden();
  });

  test("with scripting off the server still refuses it, in the same words", async ({ browser }) => {
    // The property that keeps the client tier honest: it is an accelerator, never the gate.
    const context = await browser.newContext({ javaScriptEnabled: false });
    const page = await context.newPage();
    await page.goto("/login");
    await page.click('button[type="submit"]');

    await expect(page.locator('[data-error-for="Username"]')).toHaveText("Enter your username.");
    await expect(page.locator('[data-error-for="Password"]')).toHaveText("Enter your password.");
    await context.close();
  });

  test("a blank submit never charges the account's lockout counter", async ({ page }) => {
    // The reason the guard exists at all (LoginValidationTests documents it): Identity counts a
    // wrong password against the *account*, so stray Enter presses on a shared counter PC would
    // lock out a colleague. Ten blank submits against a real username must cost nothing.
    await page.goto("/login");
    for (let i = 0; i < 10; i++) {
      await page.fill('input[name="Username"]', "farid");
      await page.fill('input[name="Password"]', "");
      await page.click('button[type="submit"]');
      await expect(page.locator('[data-error-for="Password"]')).toBeVisible();
    }

    await page.fill('input[name="Password"]', "Demo#1234");
    await page.click('button[type="submit"]');
    await expect(page).not.toHaveURL(/\/login/);      // signed in, not locked out
  });
});

test.describe("spec 0040 — signing out remembers the screen", () => {
  test("sign out from a page, sign in, land back on it", async ({ page }) => {
    const target = "/billing/opd";
    await page.goto("/login");
    await page.fill('input[name="Username"]', "rasel");
    await page.fill('input[name="Password"]', "Demo#1234");
    await page.click('button[type="submit"]');
    await page.goto(target);

    await page.click('form[action="/logout"] button[type="submit"]');
    // The page they were on rode along, so the login form can hand it back.
    await expect(page).toHaveURL(/\/login\?returnUrl=/);

    await page.fill('input[name="Username"]', "rasel");
    await page.fill('input[name="Password"]', "Demo#1234");
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(new RegExp(target.replace("/", "\\/")));
  });

  test("a hostile returnUrl lands on the dashboard, not an error page and not off-site", async ({ page }) => {
    await page.goto("/login?returnUrl=https%3A%2F%2Fevil.example%2F");
    await page.fill('input[name="Username"]', "farid");
    await page.fill('input[name="Password"]', "Demo#1234");
    await page.click('button[type="submit"]');

    // LocalRedirect used to throw here, which the fault boundary rendered as a 500.
    await expect(page).toHaveURL(/localhost:5199\/$/);
    await assertNotDevExceptionPage(page);
  });
});
