const { chromium } = require("playwright-core");
const path = require("path");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const filePath = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
  await page.goto(filePath, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(3000);

  const items = await page.evaluate(() => {
    const aside = document.querySelector("aside nav") || document.querySelector("aside");
    if (!aside) return null;
    const buttons = aside.querySelectorAll("button");
    return Array.from(buttons).map((b, i) => ({ i, text: b.textContent.trim(), cls: b.className }));
  });
  console.log(JSON.stringify(items, null, 2));

  await browser.close();
})();
