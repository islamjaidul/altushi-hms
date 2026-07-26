import { defineConfig, devices } from "@playwright/test";

/**
 * UI smoke suite for the HMS ERP app (spec 0012-ui-pass). Assumes the app is already running
 * at baseURL — this suite does not start/stop it (see README for the DB-reset + seed-flow
 * prerequisite that must run first so pages aren't empty).
 */
export default defineConfig({
  testDir: "./tests",
  timeout: 30_000,
  expect: { timeout: 5_000 },
  fullyParallel: false, // workers share one app/DB instance; keep it predictable
  workers: 2,
  retries: 0,
  reporter: [["list"], ["html", { open: "never", outputFolder: "playwright-report" }]],
  globalSetup: "./global-setup.ts",
  use: {
    baseURL: "http://localhost:5199",
    viewport: { width: 1366, height: 768 },
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
