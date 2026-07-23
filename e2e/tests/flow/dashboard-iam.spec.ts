import { test, expect } from "../../support/test-base";

test.describe("Dashboard IAM", () => {
  test.use({ storageState: "fixtures/auth.json" });

  test("dashboard-iam", async ({ page }) => {
    const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
    if (holdMs > 0) test.setTimeout(holdMs + 60_000);

    await page.goto("https://iam.seliseblocks.com/app/console");

    await page.getByRole("button", { name: "Testing" }).first().click();

    await page.getByRole("button", { name: "Authentication" }).click();

    await page.getByRole("button", { name: "Authentication" }).click();

    if (holdMs > 0) {
      await page.waitForTimeout(holdMs);
    }
  });
});