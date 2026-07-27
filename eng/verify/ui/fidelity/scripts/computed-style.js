const { chromium } = require("playwright-core");
const path = require("path");
const REF_FILE = "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2000);
  const buttons = page.locator("aside nav button");
  await buttons.nth(13).click(); // Report Delivery
  await page.waitForTimeout(800);

  const el = page.locator("text=Ready").first();
  const style = await el.evaluate((e) => {
    const cs = getComputedStyle(e);
    const rect = e.getBoundingClientRect();
    return { borderRadius: cs.borderRadius, height: rect.height, width: rect.width, padding: cs.padding, bg: cs.backgroundColor, color: cs.color, fontSize: cs.fontSize, fontWeight: cs.fontWeight, letterSpacing: cs.letterSpacing, textTransform: cs.textTransform, className: e.className };
  });
  console.log("STATUS PILL:", JSON.stringify(style, null, 2));

  // Also check nav active item bg + button (Register & print) primary btn
  await buttons.nth(1).click();
  await page.waitForTimeout(500);
  const btn = page.locator("button:has-text('Register & print ID card')").first();
  const btnStyle = await btn.evaluate((e) => {
    const cs = getComputedStyle(e);
    const rect = e.getBoundingClientRect();
    return { borderRadius: cs.borderRadius, height: rect.height, bg: cs.backgroundColor, fontSize: cs.fontSize, fontWeight: cs.fontWeight, padding: cs.padding };
  });
  console.log("PRIMARY BTN:", JSON.stringify(btnStyle, null, 2));

  const activeNav = page.locator("aside nav button").nth(1);
  const navStyle = await activeNav.evaluate((e) => {
    const cs = getComputedStyle(e);
    const rect = e.getBoundingClientRect();
    return { borderRadius: cs.borderRadius, height: rect.height, bg: cs.backgroundColor, fontSize: cs.fontSize };
  });
  console.log("ACTIVE NAV ITEM:", JSON.stringify(navStyle, null, 2));

  // table header on patient directory
  await buttons.nth(2).click();
  await page.waitForTimeout(500);
  const th = page.locator("th").first();
  const thStyle = await th.evaluate((e) => {
    const cs = getComputedStyle(e);
    return { bg: cs.backgroundColor, fontSize: cs.fontSize, fontWeight: cs.fontWeight, letterSpacing: cs.letterSpacing, textTransform: cs.textTransform, color: cs.color, padding: cs.padding };
  });
  console.log("TABLE HEADER:", JSON.stringify(thStyle, null, 2));

  const row = page.locator("tbody tr").first();
  const rowH = await row.evaluate(e => e.getBoundingClientRect().height);
  console.log("ROW HEIGHT:", rowH);

  await browser.close();
})().catch((e) => { console.error(e); process.exit(1); });
