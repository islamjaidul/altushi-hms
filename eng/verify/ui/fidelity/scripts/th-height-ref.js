const { chromium } = require("playwright-core");
const path = require("path");
const REF_FILE = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2000);
  await page.locator("aside nav button").nth(2).click();
  await page.waitForTimeout(500);
  const th = page.locator("th").first();
  const h = await th.evaluate(e => e.getBoundingClientRect().height);
  console.log("REF TH HEIGHT:", h);
  const trh = await page.locator("tbody tr").first().evaluate(e => e.getBoundingClientRect().height);
  console.log("REF ROW HEIGHT:", trh);
  await browser.close();
})();
