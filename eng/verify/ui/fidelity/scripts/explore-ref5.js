const { chromium } = require("playwright-core");
const path = require("path");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const filePath = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
  await page.goto(filePath, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(3000);

  // Click Hospital Settings (index 48) and see what appears
  const buttons = page.locator("aside nav button");
  await buttons.nth(48).click();
  await page.waitForTimeout(1500);
  await page.screenshot({ path: path.resolve(__dirname, "../ref/_explore_settings.png") });

  const bodyText = await page.evaluate(() => document.body.innerText.slice(0, 2000));
  console.log(bodyText);

  await browser.close();
})();
