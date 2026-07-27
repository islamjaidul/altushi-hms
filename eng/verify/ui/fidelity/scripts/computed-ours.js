const { chromium } = require("playwright-core");
const path = require("path");
const PASSWORD = "Demo#1234";

(async () => {
  const browser = await chromium.launch();
  const context = await browser.newContext({ baseURL: "http://localhost:5199", viewport: { width: 1366, height: 768 } });
  const page = await context.newPage();
  await page.goto("/login");
  await page.fill('input[name="Username"]', "jashim");
  await page.fill('input[name="Password"]', PASSWORD);
  await Promise.all([
    page.waitForURL((url) => !url.pathname.startsWith("/login")),
    page.click('button[type="submit"]'),
  ]);
  await page.goto("/registration", { waitUntil: "networkidle" });
  await page.waitForTimeout(500);

  const asideWidth = await page.locator("aside.sidebar").first().evaluate(e => e.getBoundingClientRect().width);
  console.log("OURS SIDEBAR WIDTH:", asideWidth);

  const topbar = await page.locator("header.topbar").first().evaluate(e => e.getBoundingClientRect().height);
  console.log("OURS TOPBAR HEIGHT:", topbar);

  const th = page.locator("th").first();
  const thStyle = await th.evaluate(e => { const cs=getComputedStyle(e); return {padding: cs.padding, fontSize: cs.fontSize, height: e.getBoundingClientRect().height}; });
  console.log("OURS TH:", thStyle);

  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
