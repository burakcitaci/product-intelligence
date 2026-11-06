import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/browser", timeout: 30000, expect: { timeout: 8000 },
  fullyParallel: false, workers: 1,
  outputDir: "../../.artifacts/browser-results",
  reporter: [["list"], ["json", { outputFile: "../../.artifacts/browser-results/report.json" }]],
  use: { baseURL: "http://127.0.0.1:5197", channel: process.env.PLAYWRIGHT_CHANNEL ?? (process.platform === "win32" ? "msedge" : "chromium"), headless: true, screenshot: "only-on-failure", trace: "retain-on-failure" },
  projects: [{ name: "desktop", use: { viewport: { width: 1365, height: 900 } } }, { name: "mobile", use: { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true } }],
  webServer: { command: "npm run build && npm run preview -- --port 5197 --strictPort", url: "http://127.0.0.1:5197", reuseExistingServer: false, timeout: 30000 },
});
