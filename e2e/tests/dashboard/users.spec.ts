import { test } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";
import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";

async function enterUsers(page: Page) {
  await Promise.all([
    page.waitForURL(/\/app\/[^/]+\/dashboard$/),
    page.getByRole("button", { name: "Testing" }).click(),
  ]);

  await Promise.all([
    page.waitForURL(/\/users$/),
    page.getByRole("link", { name: /^Users$/ }).click(),
  ]);
}

test.describe("Users", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("navigates from console to Users", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterUsers(page);
  });

  test("invites a new user", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterUsers(page);

    await page.getByRole("button", { name: "Invite User" }).click();

    const dialog = page.getByRole("dialog", { name: "Invite User" });
    await dialog.getByRole("textbox", { name: "Email" }).fill(
      `e2etest.user.${Date.now()}@yopmail.com`,
    );
    await dialog.getByRole("textbox", { name: "First name" }).fill("E2E User1");
    await dialog.getByRole("textbox", { name: "Last name" }).fill("Test");

    const submitButton = dialog.getByRole("button", { name: "Send invite" });
    await submitButton.click({ timeout: 10_000 });
  });
});