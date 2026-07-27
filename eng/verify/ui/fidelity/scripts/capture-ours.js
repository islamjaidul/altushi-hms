// Screenshots our app's screens for the visual-fidelity audit against the reference demo.
// Logs in per user (real POST /login, same as global-setup.ts) then navigates directly to each
// route. Not part of the permanent Playwright suite. Run with: node capture-ours.js
const { chromium } = require("playwright-core");
const path = require("path");

const BASE_URL = "http://localhost:5199";
const OUT_DIR = path.resolve(__dirname, "../ours");
const PASSWORD = "Demo#1234";

const SCREENS = [
  { user: "md", path: "/dashboard", name: "01-dashboard.png" },
  { user: "jashim", path: "/registration/new", name: "02-patient-registration.png" },
  { user: "jashim", path: "/registration", name: "03-patient-directory.png" },
  { user: "rasel", path: "/billing/opd", name: "04-opd-diagnosis-billing.png" },
  { user: "rasel", path: "/diagnostics/order", name: "05-test-order-invoice.png" },
  { user: "ripon", path: "/lis/board", name: "06-lis-work-board.png" },
  { user: "ripon", path: "/lis/results", name: "07-result-entry.png" },
  { user: "rasel", path: "/diagnostics/delivery", name: "08-report-delivery.png" },
  { user: "admin", path: "/admin/approvals", name: "09-voucher-approval.png" },
  { user: "rasel", path: "/billing/day-close", name: "10-daily-collection.png" },
  { user: "admin", path: "/admin/masters", name: "11-hospital-settings.png" },
];

async function login(browser, user) {
  const context = await browser.newContext({ baseURL: BASE_URL, viewport: { width: 1366, height: 768 } });
  const page = await context.newPage();
  await page.goto("/login");
  await page.fill('input[name="Username"]', user);
  await page.fill('input[name="Password"]', PASSWORD);
  await Promise.all([
    page.waitForURL((url) => !url.pathname.startsWith("/login")),
    page.click('button[type="submit"]'),
  ]);
  return { context, page };
}

(async () => {
  const browser = await chromium.launch();
  const sessions = {};
  for (const s of SCREENS) {
    if (!sessions[s.user]) {
      sessions[s.user] = await login(browser, s.user);
      console.log(`logged in ${s.user}`);
    }
    const { page } = sessions[s.user];
    await page.goto(s.path, { waitUntil: "networkidle" });
    await page.waitForTimeout(600);
    const outPath = path.join(OUT_DIR, s.name);
    await page.screenshot({ path: outPath });
    console.log(`captured ${s.user} ${s.path} -> ${outPath}`);
  }

  for (const key of Object.keys(sessions)) {
    await sessions[key].context.close();
  }
  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
