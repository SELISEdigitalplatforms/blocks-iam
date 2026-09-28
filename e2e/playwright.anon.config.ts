import { defineConfig, devices } from "@playwright/test";
import dotenv from "dotenv";
import path from "path";
dotenv.config({ path: path.resolve(__dirname, ".env.e2e") });
const baseURL = (process.env.E2E_BASE_URL || "").replace(/\/$/, "");
if (!baseURL) throw new Error("E2E_BASE_URL required");
export default defineConfig({
  testDir: "./tests",
  testMatch: [
    "**/signup-links-redemption.spec.ts",
    "**/signup-links-redemption-phase4.spec.ts",
  ],
  fullyParallel: false,
  workers: 1,
  reporter: [["list"]],
  timeout: 120_000,
  use: {
    baseURL,
    ignoreHTTPSErrors: true,
    trace: "on-first-retry",
    screenshot: "only-on-failure",
  },
  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
  ],
});
