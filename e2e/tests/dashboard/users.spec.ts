import { test } from "../../support/test-base";

const baseUrl = process.env.E2E_BASE_URL ?? "https://iam.seliseblocks.com";

test.describe("Users", () => {
  test.use({ storageState: "fixtures/auth.json" });

  test("navigates from console to Users", async ({ page }) => {
    const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
    if (holdMs > 0) test.setTimeout(holdMs + 60_000);

    await page.goto(`${baseUrl}/app/console`);

    await page.getByRole("link", { name: /^Users$/ }).click();

    await page.waitForURL(/\/users$/);

    if (holdMs > 0) {
      await page.waitForTimeout(holdMs);
    }
  });

  // test("invites a new user", async ({ page }) => {
  //   const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
  //   if (holdMs > 0) test.setTimeout(holdMs + 60_000);

  //   await page.goto(`${baseUrl}/app/users`);

  //   await page.getByRole("button", { name: "Invite User" }).click();

  //   const emailField = page.getByRole("textbox", { name: "Email" });
  //   await emailField.fill("e2etest.user1@yopmail.com");

  //   await page.getByRole("textbox", { name: "First name" }).fill("E2E User1");
  //   await page.getByRole("textbox", { name: "Last name" }).fill("Test");

  //   await page.getByRole("button", { name: "Send invite" }).click();

  //   if (holdMs > 0) {
  //     await page.waitForTimeout(holdMs);
  //   }
  // });
});