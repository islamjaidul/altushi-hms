const { chromium } = require("playwright-core");
const path = require("path");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const filePath = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
  await page.goto(filePath, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(3000);

  const el = await page.locator("text=Patient Registration").first();
  const count = await page.locator("text=Patient Registration").count();
  console.log("count matches text 'Patient Registration':", count);
  if (count > 0) {
    const html = await el.evaluate(e => e.outerHTML.slice(0, 500));
    console.log(html);
    const tagChain = await el.evaluate(e => {
      let chain = [];
      let cur = e;
      while (cur && chain.length < 6) { chain.push(cur.tagName + (cur.className ? '.'+cur.className : '')); cur = cur.parentElement; }
      return chain;
    });
    console.log(tagChain);
  }

  await browser.close();
})();
