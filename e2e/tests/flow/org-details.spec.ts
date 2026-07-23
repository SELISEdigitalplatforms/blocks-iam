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

test.describe("Organization details", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("invite-member-existing-user", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterOrganizations(page);

    // The "Invite Member" button lives inside an individual org's Members
    // tab, not on the list page header. Open the first organization card,
    // switch to its Members tab, then invite.
    const firstOrg = page
      .getByRole("button", { name: /^E2E Org/ })
      .first();
    await firstOrg.click();

    await page.getByRole("tab", { name: /^Members \(\d+\)$/ }).click();

    await page.getByRole("button", { name: "Invite Member" }).click();
    await page
      .getByRole("textbox", { name: "Email" })
      .fill("e2eorg@yopmail.com");

    await page.getByRole("combobox").click();
    await page.getByRole("dialog", { name: "Invite Member" }).click();
    await page.getByRole("button", { name: "Grant access" }).click();
  });

  test("invite-member-new-user", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
    await enterOrganizations(page);

    const orgName = `E2E Org ${Date.now()}`;
    await page.getByRole("button", { name: "Add Organization" }).click();
    await page.getByRole("textbox", { name: "Name" }).fill(orgName);
    await page.getByRole("button", { name: "Add" }).click();

  
    const orgCard = page.getByRole("button", { name: new RegExp(orgName) });
    await orgCard.waitFor({ state: "visible", timeout: 30_000 });


    await orgCard.click();

    await page.getByRole("tab", { name: /^Members \(\d+\)$/ }).click();

    await page.getByRole("button", { name: "Invite Member" }).click();

    const inviteDialog = page.getByRole("dialog", { name: "Invite Member" });
    const inviteEmail = `e2eorg.${Date.now()}@yopmail.com`;
    await inviteDialog.getByRole("textbox", { name: "Email" }).fill(inviteEmail);

    await inviteDialog.getByRole("combobox").click();
    await page
      .getByRole("button", { name: new RegExp(orgName) })
      .click();

    await inviteDialog
      .getByRole("textbox", { name: "First name" })
      .fill("E2E");
    await inviteDialog
      .getByRole("textbox", { name: "Last name" })
      .fill("Org User");

    await inviteDialog
      .getByRole("button", { name: "Send invite" })
      .click({ timeout: 10_000 });
  });
});
