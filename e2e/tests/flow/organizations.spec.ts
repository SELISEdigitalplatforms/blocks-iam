import { test, expect } from "../../support/test-base";
import { enterProjectDashboard, gotoSidebar } from "../../support/nav-helper";
import type { Page } from "@playwright/test";

async function enterOrganizations(page: Page, baseURL: string | undefined) {
  await page.goto(`${baseURL}/app/console`);
  await enterProjectDashboard(page);
  await gotoSidebar(page, "Organizations", /\/organizations$/);
}

test.describe("Organizations", () => {
  // Reuse the session saved by auth/login.spec.ts (runs first, workers=1).
  test.use({ storageState: "fixtures/auth.json" });

  test("navigates from console to the Organizations page", async ({
    page,
    baseURL,
  }) => {
    test.setTimeout(90_000);
    await enterOrganizations(page, baseURL);

    await expect(page).toHaveURL(/\/organizations$/);
    await expect(
      page.getByRole("heading", { name: "Organizations", level: 1 }),
    ).toBeVisible({ timeout: 20_000 });
  });

  test("shows either the create control or the feature-disabled state", async ({
    page,
    baseURL,
  }) => {
    test.setTimeout(90_000);
    await enterOrganizations(page, baseURL);

    // "Multiple Organizations" is a per-project feature toggle. When enabled the
    // page exposes "Add Organization"; when disabled it renders the
    // "Configure Organization" empty state. Assert the real outcome for the
    // project under test rather than assuming one.
    const addBtn = page.getByRole("button", { name: "Add Organization" });
    const configureBtn = page.getByRole("button", {
      name: "Configure Organization",
    });

    const state = await Promise.race([
      addBtn
        .waitFor({ state: "visible", timeout: 20_000 })
        .then(() => "enabled" as const)
        .catch(() => "unknown" as const),
      configureBtn
        .waitFor({ state: "visible", timeout: 20_000 })
        .then(() => "disabled" as const)
        .catch(() => "unknown" as const),
    ]);
    expect(["enabled", "disabled"]).toContain(state);

    if (state === "enabled") {
      const orgName = `E2E Org ${Date.now()}`;
      await addBtn.click();
      const addDialog = page.getByRole("dialog", { name: "Add Organization" });
      await addDialog.getByRole("textbox", { name: "Name" }).fill(orgName);
      await addDialog.getByRole("button", { name: "Add" }).click();
      await addDialog.waitFor({ state: "detached", timeout: 20_000 });
      await expect(
        page.getByRole("button", { name: new RegExp(orgName) }).first(),
      ).toBeVisible({ timeout: 30_000 });
    } else {
      await expect(
        page.getByRole("heading", {
          name: "Multiple Organizations is not enabled",
        }),
      ).toBeVisible({ timeout: 10_000 });
    }
  });
});
