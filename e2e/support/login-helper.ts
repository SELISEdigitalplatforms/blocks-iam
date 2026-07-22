import { expect } from "@playwright/test";
import type { Page } from "@playwright/test";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";
const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

export async function loginFresh(page: Page) {
  await page.goto(`${baseUrl}/login`);
  await page.getByRole("button", { name: "Log in to your account" }).click();

  const emailField = page.locator("#oidc-email");
  await emailField.waitFor({ timeout: 30_000 });
  await emailField.fill(username!);
  await page.locator("#oidc-password").fill(password!);
  await page.getByRole("button", { name: "Login", exact: true }).click();

  await page.waitForURL(/\/app\/console/, { timeout: 45_000 });
  await expect(
    page.getByRole("heading", { name: "Your Blocks Projects" }),
  ).toBeVisible({ timeout: 20_000 });
}