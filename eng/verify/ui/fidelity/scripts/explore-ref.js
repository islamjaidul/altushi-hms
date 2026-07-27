// One-off exploration script: dump the reference demo's sidebar nav structure so we can
// figure out selectors for each screen we need to screenshot. Not part of the permanent suite.
const { chromium } = require("playwright-core");
const path = require("path");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  const filePath = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
  console.log("Opening", filePath);
  await page.goto(filePath, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(3000);

  // Dump top-level structure
  const title = await page.title();
  console.log("TITLE:", title);

  // Try to find sidebar nav links
  const navLinks = await page.evaluate(() => {
    const results = [];
    const candidates = document.querySelectorAll("a, [role='button'], .nav-link, li");
    candidates.forEach((el) => {
      const text = el.textContent?.trim();
      if (text && text.length > 0 && text.length < 60) {
        const rect = el.getBoundingClientRect();
        if (rect.left < 300 && rect.width > 0 && rect.height > 0) {
          results.push({ tag: el.tagName, text, cls: el.className, x: rect.x, y: rect.y, w: rect.width, h: rect.height });
        }
      }
    });
    return results;
  });
  console.log("NAV CANDIDATES (left column, x<300):", navLinks.length);
  navLinks.slice(0, 200).forEach((n) => console.log(JSON.stringify(n)));

  await page.screenshot({ path: path.resolve(__dirname, "../ref/_explore_initial.png"), fullPage: false });
  await browser.close();
})();
