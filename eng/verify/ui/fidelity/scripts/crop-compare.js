const { chromium } = require("playwright-core");
const path = require("path");

const REF_FILE = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
const OUT = path.resolve(__dirname, "../crops");

(async () => {
  const browser = await chromium.launch();

  // Reference: report delivery status pills
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2000);
  const buttons = page.locator("aside nav button");
  await buttons.nth(13).click(); // Report Delivery
  await page.waitForTimeout(800);
  await page.screenshot({ path: path.join(OUT, "ref-status-pills.png"), clip: { x: 1140, y: 140, width: 226, height: 100 } });

  // Reference: nav section header + active item (for dept-dot / active bg comparison)
  await page.screenshot({ path: path.join(OUT, "ref-nav-zoom.png"), clip: { x: 0, y: 80, width: 226, height: 230 } });

  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
