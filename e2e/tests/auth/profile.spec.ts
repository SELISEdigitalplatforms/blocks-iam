import { test } from "../../support/test-base";
import { loginFresh } from "../../support/login-helper";

test.describe("Profile", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test("profile", async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);

    await page.getByRole("button", { name: "Open user menu" }).click();
    await page.getByRole("menuitem", { name: "My Profile" }).click();

    // Security is the default landing tab, where MFA + password live.
    await page.getByRole("button", { name: "Enable" }).first().click();
    await page.getByRole("dialog").locator("[data-input-otp]").fill("");
    await page.getByRole("button", { name: "Close" }).click();

    await page.getByRole("button", { name: "Enable" }).nth(1).click();
    await page.getByRole("dialog").locator("[data-input-otp]").fill("");
    await page.getByRole("button", { name: "Close" }).click();

    await page.getByRole("button", { name: "Update Password" }).click();
    await page.getByRole("button", { name: "Close" }).click();

    await page.getByRole("tab", { name: "History" }).click();
    await page.getByRole("tab", { name: "Sessions" }).click();
  });
});
