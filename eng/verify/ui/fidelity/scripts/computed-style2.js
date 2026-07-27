const { chromium } = require("playwright-core");
const path = require("path");
const REF_FILE = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2000);
  const buttons = page.locator("aside nav button");

  await buttons.nth(1).click();
  await page.mouse.move(700, 700); // move away so :hover doesn't linger
  await page.waitForTimeout(300);
  const navStyle = await buttons.nth(1).evaluate((e) => {
    const cs = getComputedStyle(e);
    const rect = e.getBoundingClientRect();
    return { borderRadius: cs.borderRadius, height: rect.height, bg: cs.backgroundColor, fontSize: cs.fontSize, fontWeight: cs.fontWeight, cls: e.className };
  });
  console.log("ACTIVE NAV ITEM (mouse away):", JSON.stringify(navStyle, null, 2));
  // hover a DIFFERENT inactive item to see hover color distinctly
  await buttons.nth(3).hover();
  await page.waitForTimeout(200);
  const hoverStyle = await buttons.nth(3).evaluate((e) => {
    const cs = getComputedStyle(e);
    return { bg: cs.backgroundColor };
  });
  console.log("HOVERED INACTIVE ITEM:", JSON.stringify(hoverStyle));
  const idleStyle = await buttons.nth(4).evaluate((e) => {
    const cs = getComputedStyle(e);
    return { bg: cs.backgroundColor, color: cs.color };
  });
  console.log("IDLE ITEM:", JSON.stringify(idleStyle));

  // pill parent container for status badge
  await buttons.nth(13).click();
  await page.waitForTimeout(600);
  const pillSpan = page.locator("text=Ready").first();
  const parentStyle = await pillSpan.evaluate((e) => {
    let node = e;
    for (let i=0;i<4;i++){
      const cs = getComputedStyle(node);
      if (cs.borderRadius !== "0px" || cs.backgroundColor !== "rgba(0, 0, 0, 0)") {
        return { level:i, tag: node.tagName, cls: node.className, borderRadius: cs.borderRadius, height: node.getBoundingClientRect().height, padding: cs.padding, bg: cs.backgroundColor, color: cs.color, fontWeight: cs.fontWeight, fontSize: cs.fontSize };
      }
      node = node.parentElement;
    }
    return null;
  });
  console.log("PILL CONTAINER:", JSON.stringify(parentStyle, null, 2));

  // table td padding + row height on Patient Directory
  await buttons.nth(2).click();
  await page.waitForTimeout(500);
  const td = page.locator("tbody tr td").first();
  const tdStyle = await td.evaluate(e => { const cs=getComputedStyle(e); return {padding: cs.padding, fontSize: cs.fontSize, borderBottom: cs.borderBottom}; });
  console.log("TD STYLE:", JSON.stringify(tdStyle));

  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
