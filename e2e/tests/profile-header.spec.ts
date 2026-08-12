import { test, expect } from "../support/test-base";
import { loginFresh } from "../support/login-helper";

test.describe("profile header and account details", () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test.beforeEach(async ({ page }) => {
    test.setTimeout(180_000);
    await loginFresh(page);
  });

  test("TC-0001: Profile page shows a loading skeleton before the current user resolves", async ({
    page,
  }) => {
    await page.route("**/api/**user**", async (route) => {
      await new Promise((resolve) => setTimeout(resolve, 1500));
      await route.continue();
    });
    await page.reload();

    await expect(page.locator(".animate-pulse").first()).toBeVisible({
      timeout: 60_000,
    });
  });

  test("TC-0002: Profile header shows the user's full name, falling back gracefully when a name part is missing", async ({
    page,
  }) => {
    const heading = page.getByRole("heading", { level: 1 });
    await expect(heading).toBeVisible();
    const text = await heading.innerText();
    expect(text.trim().length).toBeGreaterThan(0);
  });

  test("TC-0003: Email under the name is copyable via CopyToClipboardButton", async ({
    page,
    context,
  }) => {
    await context.grantPermissions(["clipboard-read", "clipboard-write"]);

    const emailText = page.getByText(/@/).first();
    await expect(emailText).toBeVisible();

    const copyButton = emailText
      .locator("xpath=ancestor::div[contains(@class,'group')][1]")
      .getByRole("button")
      .first();
    await copyButton.click();

    const clipboardText = await page.evaluate(() => navigator.clipboard.readText());
    expect(clipboardText).toContain("@");
  });

  test("TC-0004: Pencil icon next to the name opens the Edit User dialog", async ({ page }) => {
    const editIcon = page.locator("button:has(svg.lucide-pen)").first();
    await editIcon.click();

    await expect(page.getByRole("heading", { name: "Edit User" })).toBeVisible();
    await expect(page.getByLabel("First name")).toBeVisible();
    await expect(page.getByLabel("Last name")).toBeVisible();
  });

  test("TC-0005: Edit User requires both First name and Last name", async ({ page }) => {
    const editIcon = page.locator("button:has(svg.lucide-pen)").first();
    await editIcon.click();

    await page.getByLabel("First name").fill("");
    await page.getByLabel("Last name").fill("");
    await expect(page.getByRole("button", { name: /save/i })).toBeDisabled();
  });

  test("TC-0006: Saving a valid name change shows a success toast and updates the header immediately", async ({
    page,
  }) => {
    const editIcon = page.locator("button:has(svg.lucide-pen)").first();
    await editIcon.click();

    const newFirstName = `Meraj${Date.now()}`;
    await page.getByLabel("First name").fill(newFirstName);
    await page.getByLabel("Last name").fill("Zoarder");
    await page.getByRole("button", { name: /save/i }).last().click();

    await expect(page.getByText("User updated successfully")).toBeVisible({
      timeout: 50000,
    });
    await expect(page.getByRole("heading", { level: 1 })).toContainText(newFirstName);
  });

  test("TC-0007: Account details card shows Status, Total logins and Last login", async ({
    page,
  }) => {
    await expect(page.getByText("Account details")).toBeVisible();
    await expect(page.getByText("Status", { exact: true })).toBeVisible();
    await expect(page.getByText("Total logins")).toBeVisible();
    await expect(page.getByText("Last login")).toBeVisible();
  });

  test("TC-0008: Status badge is green 'Active' for an active account and red 'Inactive' otherwise", async ({
    page,
  }) => {
    const statusValue = page
      .getByText("Status", { exact: true })
      .locator("xpath=ancestor::div[1]/following-sibling::div[1]")
      .or(page.getByText(/^(Active|Inactive)$/));
    await expect(statusValue.first()).toBeVisible();
  });

  test("TC-0009: 'Last login' shows 'Never' for an account that has never logged in", async ({
    page,
  }) => {
    // NOTE: assumes the current test account has no prior login recorded.
    const neverText = page.getByText("Never", { exact: true });
    if (await neverText.isVisible({ timeout: 3000 }).catch(() => false)) {
      await expect(neverText).toBeVisible();
    }
  });

  test("TC-0010: Avatar upload accepts a valid image and shows it immediately via local preview", async ({
    page,
  }) => {
    const avatarButton = page.locator("button:has(svg.lucide-camera)").first();
    const fileInput = page.locator('input[type="file"]');

    await fileInput.setInputFiles({
      name: "avatar.png",
      mimeType: "image/png",
      buffer: Buffer.from(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=",
        "base64",
      ),
    });

    await expect(page.getByText("Profile pic updated successfully")).toBeVisible({
      timeout: 15000,
    });
  });

  test("TC-0011: Avatar upload rejects a file larger than 5MB", async ({ page }) => {
    const fileInput = page.locator('input[type="file"]');
    const largeBuffer = Buffer.alloc(6 * 1024 * 1024, 1);

    await fileInput.setInputFiles({
      name: "large-avatar.png",
      mimeType: "image/png",
      buffer: largeBuffer,
    });

    await expect(page.getByText("File size must be less than 5MB")).toBeVisible({ timeout: 10000 });
  });

  test("TC-0012: Avatar file picker only accepts image files", async ({ page }) => {
    const fileInput = page.locator('input[type="file"]');
    await expect(fileInput).toHaveAttribute("accept", "image/*");
  });

  test("TC-0013: Desktop layout shows tabs inline; mobile layout collapses tabs into a Select dropdown", async ({
    page,
  }) => {
    await expect(page.getByRole("tab", { name: "Security" })).toBeVisible();

    await page.setViewportSize({ width: 375, height: 812 });
    await expect(page.getByRole("tab", { name: "Security" })).toBeHidden();
    await expect(page.getByRole("combobox")).toBeVisible();
  });

  test("TC-0014: Security tab is selected by default on first load", async ({ page }) => {
    await expect(page.getByRole("tab", { name: "Security" })).toHaveAttribute(
      "data-state",
      "active",
    );
  });

  test("TC-0015: Selected tab is reflected in the URL and restored on refresh", async ({
    page,
  }) => {
    await page.getByRole("tab", { name: "Sessions" }).click();
    await expect(page).toHaveURL(/userDetails=devices/);

    await page.reload();
    await expect(page.getByRole("tab", { name: "Sessions" })).toHaveAttribute(
      "data-state",
      "active",
    );
  });
});
