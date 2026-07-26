import { test, expect } from "@playwright/test";
import { ROUTES, USERS, authFile } from "../helpers/users";

// ---- U1: role home is 3-6 large action cards, never a full menu tree -------------------------
test.describe("U1 — role home shows 3-6 action cards", () => {
  for (const user of Object.keys(USERS)) {
    test(`${user} (${USERS[user].role}) sees between 3 and 6 .action-card elements on /`, async ({ browser }) => {
      const context = await browser.newContext({ storageState: authFile(user) });
      const page = await context.newPage();
      await page.goto("/");
      const count = await page.locator(".action-card").count();
      expect(count, `${user}'s home screen has ${count} action cards`).toBeGreaterThanOrEqual(3);
      expect(count, `${user}'s home screen has ${count} action cards`).toBeLessThanOrEqual(6);
      await context.close();
    });
  }
});

// ---- U3: base font floor, primary-button tap target, no body-level horizontal scroll ----------
test.describe("U3 — readable base font, 44px tap targets, no horizontal body scroll @ 1366x768", () => {
  for (const route of ROUTES) {
    // Each iteration gets its own nested describe so test.use() scopes to just this route's test
    // — a bare test.use() call repeated in a shared loop silently applies only its LAST value to
    // every test in the block (they all resolve against the same suite-level fixture override).
    test.describe(route.path, () => {
      test.use({ storageState: authFile(route.user) });

      test(`base font, buttons, no h-scroll`, async ({ page }) => {
        await page.goto(route.path);

        const baseFontPx = await page.evaluate(() =>
          parseFloat(getComputedStyle(document.documentElement).fontSize),
        );
        expect(baseFontPx, "root font-size (U3 floor)").toBeGreaterThanOrEqual(16);

        const buttons = page.locator(".btn-primary, .btn-cta");
        const n = await buttons.count();
        for (let i = 0; i < n; i++) {
          const box = await buttons.nth(i).boundingBox();
          // Round: getBoundingClientRect can report e.g. 43.99998 for a 44px CSS box at this DPI
          // — that's sub-pixel rendering noise, not a real 1px-under-target defect.
          if (box) expect(Math.round(box.height), `button ${i} on ${route.path}`).toBeGreaterThanOrEqual(44);
        }

        const { scrollWidth, viewportWidth } = await page.evaluate(() => ({
          scrollWidth: document.body.scrollWidth,
          viewportWidth: window.innerWidth,
        }));
        // "greatly exceeds" the viewport — small anti-aliasing/scrollbar slop is not a defect.
        expect(scrollWidth, `document.body.scrollWidth on ${route.path}`).toBeLessThanOrEqual(viewportWidth + 40);
      });
    });
  }
});

// ---- U12: colour is never the only carrier of status — every .pill/.flag has real text --------
test.describe("U12 — .pill and .flag always carry text, not just colour", () => {
  const pages: { path: string; user: string }[] = [
    { path: "/diagnostics/delivery", user: "rasel" },
    { path: "/billing/opd", user: "rasel" },
    { path: "/lis/verify", user: "farhana" },
    { path: "/lis/results", user: "ripon" },
    { path: "/lis/board", user: "ripon" },
  ];
  for (const { path, user } of pages) {
    // Nested describe per iteration — see the U3 block above for why a bare test.use() in a
    // shared loop doesn't scope per test.
    test.describe(path, () => {
      test.use({ storageState: authFile(user) });
      let sawAny = false;

      test(`.pill/.flag have text`, async ({ page }) => {
        await page.goto(path);
        for (const selector of [".pill", ".flag"]) {
          const els = page.locator(selector);
          const n = await els.count();
          for (let i = 0; i < n; i++) {
            sawAny = true;
            const text = (await els.nth(i).innerText()).trim();
            expect(text, `${selector} #${i} on ${path} has no text`).not.toBe("");
          }
        }
        // A page with zero .pill/.flag elements isn't a U12 pass — it's untested. Fail loud so a
        // future data/permission change here can't silently turn this into a no-op again.
        expect(sawAny, `no .pill or .flag elements found on ${path} — nothing was actually checked`).toBe(true);
      });
    });
  }
});

// ---- U9: patient-context screens show name + UHID in .patient-banner once a record is selected
test.describe("U9 — patient-banner shows name + UHID on selection", () => {
  test("/lis/results (ripon)", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("ripon") });
    const page = await context.newPage();
    await page.goto("/lis/results");
    const banner = page.locator(".patient-banner");
    await expect(banner, "no order awaiting result entry was selected — worklist may be empty").toBeVisible();
    await expect(banner.locator(".patient-name")).toHaveText("Rahim Uddin");
    await expect(banner.locator(".patient-meta")).toContainText("ALT-000001");
    await context.close();
  });

  test("/lis/verify (farhana)", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("farhana") });
    const page = await context.newPage();
    await page.goto("/lis/verify");
    const banner = page.locator(".patient-banner");
    await expect(banner, "no order awaiting verification was selected — worklist may be empty").toBeVisible();
    await expect(banner.locator(".patient-name")).toHaveText("Rahim Uddin");
    await expect(banner.locator(".patient-meta")).toContainText("ALT-000001");
    await context.close();
  });
});

// ---- U7: illegal actions are absent, not merely warned about ----------------------------------
test.describe("U7 — error-proofing by absence", () => {
  test("(a) /billing/opd with no open counter session: no save button, an 'Open your counter' link instead", async ({ browser }) => {
    // shahid holds billing.invoice.create but never billing.session.open, so he can reach the
    // page but has never been able to open a counter — Session is always null for him.
    const context = await browser.newContext({ storageState: authFile("shahid") });
    const page = await context.newPage();
    await page.goto("/billing/opd");

    await expect(page.locator("[data-pay]")).toHaveCount(0);
    const openLink = page.getByRole("link", { name: /open your counter/i });
    await expect(openLink).toBeVisible();
    await expect(openLink).toHaveAttribute("href", "/billing/session");
    await context.close();
  });

  // Re-scoped after the app was fixed: payment is now the sole release trigger for the lab
  // pipeline (DiagnosticsRelease.ReleasePaidOrdersAsync), so a due can no longer exist on any
  // order that reaches the lab board at all — there's nothing left to render as "held." The
  // meaningful behaviour to check is the release seam itself: a part-paid order is invisible to
  // the bench until the balance is collected, then it appears exactly where payment-in-full would
  // have put it (with a collect control, never silently skipped).
  test("(b) a part-paid diagnostics order is absent from the lab board until the balance is collected, then appears with a collect control", async ({ browser }) => {
    // ---- create an order for TSH (needs a real tube), paid only in part ----
    const billerCtx = await browser.newContext({ storageState: authFile("rasel") });
    const billerPage = await billerCtx.newPage();
    await billerPage.goto("/diagnostics/order?PatientId=1");
    await Promise.all([
      billerPage.waitForResponse((r) => r.url().includes("/diagnostics/order") && r.request().method() === "POST"),
      billerPage.getByRole("button", { name: "Add TSH", exact: true }).click(),
    ]);
    const gross = Number(await billerPage.locator("[data-pos]").getAttribute("data-gross"));
    expect(gross, "TSH gross should be a positive price").toBeGreaterThan(0);
    const partial = Math.max(1, gross - 100); // leaves a real due > 0
    const due = gross - partial;
    await billerPage.locator('input[name="PaidNow"]').fill(String(partial));
    await Promise.all([
      billerPage.waitForURL(/\/diagnostics\/order\/\d+/),
      billerPage.locator("[data-pay]").click(),
    ]);
    const orderId = /\/diagnostics\/order\/(\d+)/.exec(billerPage.url())![1];
    const orderNo = "LB-" + orderId.padStart(5, "0");
    await expect(billerPage.locator("body")).toContainText("labels print once the balance is paid");
    await billerCtx.close();

    // ---- absent from the lab board entirely while due > 0 ----
    const labCtx1 = await browser.newContext({ storageState: authFile("ripon") });
    const labPage1 = await labCtx1.newPage();
    await labPage1.goto("/lis/board");
    await expect(labPage1.locator("body"), `${orderNo} should not appear while unpaid`).not.toContainText(orderNo);
    await labCtx1.close();

    // ---- collect the remaining balance at Due Collection ----
    const duesCtx = await browser.newContext({ storageState: authFile("rasel") });
    const duesPage = await duesCtx.newPage();
    duesPage.on("dialog", (d) => d.accept()); // the Collect form carries a data-confirm prompt
    await duesPage.goto("/billing/dues");
    // Rahim Uddin can have other outstanding dues by this point (discount-and-dues.py's "over-
    // collection is impossible" check deliberately leaves a ৳700 CON-GEN due uncollected — that's
    // the point of that check, not a cleared balance), so match on the specific due amount too,
    // not just the patient name.
    const row = duesPage
      .locator("tbody tr", { hasText: "Rahim Uddin" })
      .filter({ hasText: String(due) })
      .first();
    await expect(row, `expected an outstanding due row of ৳${due} for Rahim Uddin (the new TSH order)`).toBeVisible();
    await Promise.all([
      duesPage.waitForURL(/\/billing\/invoice\/\d+/),
      row.getByRole("button", { name: "Collect", exact: true }).click(),
    ]);
    await expect(duesPage.locator("body")).toContainText("released to the lab");
    await duesCtx.close();

    // ---- now appears on the board, offering a collect control (not silently skipped) ----
    const labCtx2 = await browser.newContext({ storageState: authFile("ripon") });
    const labPage2 = await labCtx2.newPage();
    await labPage2.goto("/lis/board");
    const card = labPage2.locator(".board-card", { hasText: orderNo });
    await expect(card, `${orderNo} should appear once its balance is settled`).toBeVisible();
    await expect(card.getByRole("button", { name: /^Collect/ })).toBeVisible();
    await labCtx2.close();
  });

  // §12: collecting/receiving/rejecting tubes is the technologist's grant, not the pathologist's
  // — even though both roles can read the worklist. Board.cshtml gates the controls on
  // lis.sample.collect, which the pathologist doesn't hold.
  test("(c) /lis/board hides Collect/Receive/Reject controls from a pathologist (lacks lis.sample.collect)", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("farhana") });
    const page = await context.newPage();
    await page.goto("/lis/board");

    await expect(page.getByRole("button", { name: /^Collect/ })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Receive at lab →", exact: true })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Reject & re-collect", exact: true })).toHaveCount(0);
    await context.close();
  });
});

// ---- U4 / §9A.4 #1: registration is completable keyboard-only -------------------------------
test.describe("U4 — keyboard-only registration", () => {
  test("Tab/typing alone reaches and activates 'Register & print ID card', and creates a real patient", async ({ browser }) => {
    const context = await browser.newContext({ storageState: authFile("jashim") });
    const page = await context.newPage();
    await page.goto("/registration/new");

    // RegistrationService.FindDuplicatesAsync matches on (dmetaphone-phonetic name match) AND
    // (age within ±2 years) — dmetaphone ignores digits, so a name that only varies by a trailing
    // number (e.g. "...Patient 172845") phonetically collides with every previous run's patient
    // that used the same fixed words, and a fixed age like "29" then falls within ±2 of itself
    // every time. Across repeated suite runs (no DB reset in between) that reliably trips the
    // app's own (real, correct) duplicate detector, re-showing this same form instead of
    // redirecting — a false failure in the test, not a defect. Vary the letters and the age too.
    const randomLetters = Array.from({ length: 8 }, () =>
      String.fromCharCode(65 + Math.floor(Math.random() * 26)),
    ).join("");
    const uniqueName = `Keyboard QA ${randomLetters}`;
    const uniqueAge = String(20 + Math.floor(Math.random() * 50));

    await page.locator('input[name="FullName"]').focus();
    await page.keyboard.type(uniqueName);
    await page.keyboard.press("Tab"); // -> AgeOrDob
    await page.keyboard.type(uniqueAge);
    await page.keyboard.press("Tab"); // -> Sex select
    await page.keyboard.press("Tab"); // -> Phone
    // Unique per run: a reused phone number across repeated suite runs trips the app's own (real,
    // correct) duplicate-patient detector, which would otherwise re-show this same form instead
    // of redirecting — a false failure in the test, not a defect.
    const uniquePhone = "017" + Math.floor(10_000_000 + Math.random() * 89_999_999);
    await page.keyboard.type(uniquePhone);
    await page.keyboard.press("Tab"); // -> BloodGroup select
    await page.keyboard.press("Tab"); // -> Guardian
    await page.keyboard.press("Tab"); // -> Area
    await page.keyboard.press("Tab"); // -> PatientType select
    await page.keyboard.press("Tab"); // -> Address
    await page.keyboard.press("Tab"); // -> Identity-unknown checkbox (must NOT toggle it)

    const primaryBtn = page.locator('button[name="action"][value="print"]');
    // Give the tab order a bounded number of extra hops in case a field is inserted/reordered —
    // if it needs more than a couple of extra Tabs to reach the primary button, that's the finding.
    for (let i = 0; i < 3 && !(await primaryBtn.evaluate((el) => el === document.activeElement)); i++) {
      await page.keyboard.press("Tab");
    }
    await expect(primaryBtn, "primary action not reached by Tab from the first field").toBeFocused();

    await Promise.all([page.waitForURL(/\/registration\/\d+\/card/), page.keyboard.press("Enter")]);

    await expect(page.locator(".sheet")).toContainText(uniqueName);
    await context.close();
  });
});
