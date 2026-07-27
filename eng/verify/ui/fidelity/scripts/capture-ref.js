// Screenshots the reference demo (docs/architecture/assets/altushi-hms-demo.html) at the
// screens we compare against our app. Not part of the permanent Playwright suite — a one-off
// audit script for the visual-fidelity pass. Run with: node capture-ref.js
const { chromium } = require("playwright-core");
const path = require("path");

const REF_FILE =
  "file://" + path.resolve(__dirname, "../../../../../docs/architecture/assets/altushi-hms-demo.html");
const OUT_DIR = path.resolve(__dirname, "../ref");

// nav button index (0-based, in DOM order inside <aside nav>) -> output screenshot name
const SCREENS = [
  { idx: 0, name: "01-dashboard.png", label: "Dashboard" },
  { idx: 1, name: "02-patient-registration.png", label: "Patient Registration" },
  { idx: 2, name: "03-patient-directory.png", label: "Patient Directory" },
  { idx: 5, name: "04-opd-diagnosis-billing.png", label: "OPD / Diagnosis Billing" },
  { idx: 9, name: "05-test-order-invoice.png", label: "Test Order Invoice" },
  { idx: 10, name: "06-lis-work-board.png", label: "LIS Work Board" },
  { idx: 11, name: "07-result-entry.png", label: "Result Entry" },
  { idx: 13, name: "08-report-delivery.png", label: "Report Delivery" },
  { idx: 33, name: "09-voucher-approval.png", label: "Voucher Approval" },
  { idx: 35, name: "10-daily-collection.png", label: "Daily Collection" },
  { idx: 48, name: "11-hospital-settings.png", label: "Hospital Settings" },
];

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1366, height: 768 } });
  await page.goto(REF_FILE, { waitUntil: "load", timeout: 60000 });
  await page.waitForTimeout(2500); // let the client-side render settle

  const buttons = page.locator("aside nav button");
  for (const s of SCREENS) {
    await buttons.nth(s.idx).click();
    await page.waitForTimeout(900);
    const outPath = path.join(OUT_DIR, s.name);
    await page.screenshot({ path: outPath });
    console.log(`captured ${s.label} -> ${outPath}`);
  }

  await browser.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
