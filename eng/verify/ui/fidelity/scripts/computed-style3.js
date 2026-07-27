const { chromium } = require("playwright-core");
const path = require("path");
const REF_FILE = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2000);

  const header = await page.evaluate(() => {
    const h = document.querySelector("header") || document.querySelector("[class]");
    return null;
  });

  // topbar-equivalent: find element containing the search input's ancestor row
  const searchBox = page.locator("input[placeholder*='Search patient']").first();
  const topbarInfo = await searchBox.evaluate((e) => {
    let node = e;
    for (let i=0;i<6;i++){ node = node.parentElement; if(!node) break; }
    return null;
  });

  const asideWidth = await page.locator("aside").first().evaluate(e => e.getBoundingClientRect().width);
  console.log("SIDEBAR WIDTH:", asideWidth);

  // header bar: element that is a direct sibling containing dashboard title AND search box, full width
  const titleEl = page.locator("text=Dashboard").first();
  await page.waitForTimeout(200);

  // find the row containing search + viewing as + bell - use bounding box height by locating notif bell icon's ancestor with height spanning full top strip
  const bell = page.locator("text=notifications").first();
  const bellBox = await bell.boundingBox();
  console.log("BELL BOX (approx header row):", bellBox);

  // sidebar top brand block height
  const brand = page.locator("text=HOSPITAL ERP").first();
  const brandAncestor = await brand.evaluate(e => {
    let n = e;
    for (let i=0;i<4;i++) n = n.parentElement;
    const r = n.getBoundingClientRect();
    return { height: r.height, width: r.width, tag: n.tagName, cls: n.className };
  });
  console.log("BRAND BLOCK:", brandAncestor);

  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
