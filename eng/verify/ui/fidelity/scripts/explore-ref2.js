const { chromium } = require("playwright-core");
const path = require("path");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const filePath = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
  await page.goto(filePath, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(3000);

  console.log("frames:", page.frames().length);
  page.frames().forEach(f => console.log("frame url:", f.url()));

  const info = await page.evaluate(() => {
    const iframes = document.querySelectorAll('iframe').length;
    const shadowHosts = [];
    document.querySelectorAll('*').forEach(el => { if (el.shadowRoot) shadowHosts.push(el.tagName + '.' + el.className); });
    return { iframes, shadowHosts, bodyChildCount: document.body.children.length, htmlLen: document.documentElement.outerHTML.length };
  });
  console.log(JSON.stringify(info, null, 2));

  await browser.close();
})();
