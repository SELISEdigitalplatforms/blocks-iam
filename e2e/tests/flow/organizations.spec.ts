import { test } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";
import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";

async function enterOrganizations(page: Page) {
  const environmentButton = page.getByRole("button", { name: "Testing" });
  await Promise.all([
    page.waitForURL(/\/app\/[^/]+\/dashboard$/),
    environmentButton.first().click(),
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

  test("add organization", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterOrganizations(page);

    const orgName = `E2E Org ${Date.now()}`;

    await page.getByRole("button", { name: "Add Organization" }).click();
    const addDialog = page.getByRole("dialog", { name: "Add Organization" });
    await addDialog.getByRole("textbox", { name: "Name" }).fill(orgName);
    await addDialog.getByRole("button", { name: "Add" }).click();
    await addDialog.waitFor({ state: "detached", timeout: 10_000 });

    await page.getByRole("tab", { name: /Members \(\d+\)/ }).click();
   
  });
});