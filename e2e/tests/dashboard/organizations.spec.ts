import { test } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";
import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";

async function enterOrganizations(page: Page) {
  await Promise.all([
    page.waitForURL(/\/app\/[^/]+\/dashboard$/),
    page.getByRole("button", { name: "Testing" }).click(),
  ]);

  await Promise.all([
    page.waitForURL(/\/organizations$/),
    page.getByRole("link", { name: /^Organizations$/ }).click(),
  ]);
}

test.describe("Organizations", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("navigates from console to Organizations", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterOrganizations(page);
  });
});