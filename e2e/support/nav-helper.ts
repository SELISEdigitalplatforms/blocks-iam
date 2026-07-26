import { expect, type Page } from "@playwright/test";

/**
 * From the console ("Your Blocks Projects"), enter the first project's
 * "Testing" environment and land on its dashboard. Returns the scoped
 * itemId parsed from the resulting URL so specs can build deep links.
 */
export async function enterProjectDashboard(page: Page): Promise<string> {
  await expect(
    page.getByRole("heading", { name: "Your Blocks Projects" }),
  ).toBeVisible({ timeout: 20_000 });

  await Promise.all([
    page.waitForURL(/\/app\/[^/]+\/dashboard$/, { timeout: 30_000 }),
    page.getByRole("button", { name: "Testing" }).first().click(),
  ]);
  await expect(page).toHaveURL(/\/app\/[^/]+\/dashboard$/);

  const match = page.url().match(/\/app\/([^/]+)\/dashboard/);
  return match ? match[1] : "";
}

/** Click a left-sidebar link by exact name and wait for the URL to settle. */
export async function gotoSidebar(
  page: Page,
  linkName: string,
  urlRe: RegExp,
): Promise<void> {
  await Promise.all([
    page.waitForURL(urlRe, { timeout: 30_000 }),
    page.getByRole("link", { name: linkName, exact: true }).click(),
  ]);
}
