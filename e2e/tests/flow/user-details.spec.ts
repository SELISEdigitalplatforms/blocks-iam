import { test } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";
import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";

async function enterUsers(page: Page) {
  const environmentButton = page.getByRole("button", { name: "Testing" });
  await Promise.all([
    page.waitForURL(/\/app\/[^/]+\/dashboard$/),
    environmentButton.first().click(),
  ]);

  await Promise.all([
    page.waitForURL(/\/users$/),
    page.getByRole("link", { name: /^Users$/ }).click(),
  ]);
}

test.describe("User details", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("user-details", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterUsers(page);

    await page
      .getByRole("button", {
        name: /ET\s+E2E User1 Test\s+e2etest\.user\.\d+@yopmail\.com\s+Inactive/,
      })
      .last()
      .click(); 


    await page.getByRole("tab", { name: "Access" }).click();

    await page.getByRole("button", { name: "Manage Roles" }).click();
    await page.getByRole("button", { name: "Cancel" }).click();

    await page.getByRole("button", { name: "Manage Permissions" }).click();
    await page.getByRole("button", { name: "Cancel" }).click();

    
    await page.getByRole("tab", { name: "Sessions" }).click();
    await page.getByRole("tab", { name: "History" }).click();


  });

  test("inactive-user-resend-activation", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterUsers(page);

    await page
      .getByRole("button", {
        name: /ET\s+E2E User1 Test\s+e2etest\.user\.\d+@yopmail\.com\s+Inactive/,
      })
      .last()
      .click();

    await page.getByRole("button", { name: "Resend Activation" }).click();
    await page.getByRole("button", { name: "Resend" }).click();
  });

});