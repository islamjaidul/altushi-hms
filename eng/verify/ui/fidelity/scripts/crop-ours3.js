const { chromium } = require("playwright-core");
const path = require("path");
const OUT = path.resolve(__dirname, "../crops");
const PASSWORD = "Demo#1234";

(async () => {
  const browser = await chromium.launch();
  const context = await browser.newContext({ baseURL: "http://localhost:5199", viewport: { width: 1366, height: 768 } });
  const page = await context.newPage();
  await page.goto("/login");
  await page.fill('input[name="Username"]', "rasel");
  await page.fill('input[name="Password"]', PASSWORD);
  await Promise.all([
    page.waitForURL((url) => !url.pathname.startsWith("/login")),
    page.click('button[type="submit"]'),
  ]);
  await page.goto("/billing/day-close", { waitUntil: "networkidle" });
  await page.waitForTimeout(500);
  await page.screenshot({ path: path.join(OUT, "ours-status-pill.png"), clip: { x: 860, y: 543, width: 130, height: 34 } });
  // also crop nav section for dept-dot/active-item comparison
  await page.screenshot({ path: path.join(OUT, "ours-nav-zoom.png"), clip: { x: 0, y: 80, width: 226, height: 230 } });
  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
