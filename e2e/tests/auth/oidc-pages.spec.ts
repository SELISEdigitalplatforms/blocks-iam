import { test, expect } from "../../support/test-base";

/**
 * OIDC ancillary pages reached at the boundaries of the auth flow:
 *
 *   - /oidc/mfa-check?mfa_id=...&mfa_type=1|2   — verify a 2FA OTP
 *   - /oidc/permission                          — consent screen (Allow / Deny)
 *   - /oidc/error                               — terminal error screen
 *
 * mfa-check posts back to /api/oidc/login with a different body shape
 * (mfa_id + mfa_code + tenant_id) than the regular login. mfa_type=1 maps to
 * a 6-slot TOTP; mfa_type=2 maps to a 5-slot email OTP plus a resend button.
 */

const mockOidcUiConfig = async (page: any) => {
  await page.route("**/api/idp/oidc-ui-config*", async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ captcha: null, template: {} }),
    });
  });
};

test.describe("OIDC MFA check", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("MFA check — email/TOTP render, invalid code + locked errors, successful redirect", async ({
    page,
  }) => {
    await test.step("[Positive] mfa_type=2 (email) renders a 5-slot OTP and the resend button", async () => {
      await page.goto("/oidc/mfa-check?mfa_id=mfa-id-1&mfa_type=2", {
        waitUntil: "domcontentloaded",
      });

      // The heading + email-mode subhead.
      await expect(
        page.getByText(/check your email for the verification code/i),
      ).toBeVisible({ timeout: 30_000 });

      // 5 input slots; the OTP component is a single input with maxLength=5.
      const otpInput = page.locator('input[maxlength="5"]');
      await expect(otpInput).toBeVisible();

      // Resend button (mfa_type=2 only).
      await expect(page.getByRole("button", { name: /resend code/i })).toBeVisible();
    });

    await test.step("[Positive] mfa_type=1 (TOTP) renders a 6-slot OTP and no resend button", async () => {
      await page.goto("/oidc/mfa-check?mfa_id=mfa-id-2&mfa_type=1", {
        waitUntil: "domcontentloaded",
      });

      await expect(
        page.getByText(/open your authenticator app/i),
      ).toBeVisible({ timeout: 30_000 });

      const otpInput = page.locator('input[maxlength="6"]');
      await expect(otpInput).toBeVisible();

      // TOTP has no resend path — the resend button must be absent.
      await expect(page.getByRole("button", { name: /resend code/i })).toHaveCount(0);
    });

    await test.step("[Negative] Server returns invalid_mfa_code → inline error + form resets", async () => {
      await page.route("**/api/oidc/login", async (route: any) => {
        const body = JSON.parse(route.request().postData() || "{}");
        // Only intercept MFA-stage requests, not regular signin requests.
        if (!body.mfa_code) {
          await route.fallback();
          return;
        }
        await route.fulfill({
          status: 401,
          contentType: "application/json",
          body: JSON.stringify({
            error: "invalid_mfa_code",
            error_description: "Invalid verification code. Please try again.",
          }),
        });
      });

      await page.goto("/oidc/mfa-check?mfa_id=mfa-id-3&mfa_type=2", {
        waitUntil: "domcontentloaded",
      });

      await page.locator('input[maxlength="5"]').fill("12345");
      await page.getByRole("button", { name: /^verify$/i }).click();

      await expect(
        page.getByText(/invalid verification code\. please try again\./i),
      ).toBeVisible({ timeout: 15_000 });

      await page.unroute("**/api/oidc/login").catch(() => {});
    });

    await test.step("[Negative] Server returns account_locked → locked-account message", async () => {
      await page.route("**/api/oidc/login", async (route: any) => {
        const body = JSON.parse(route.request().postData() || "{}");
        if (!body.mfa_code) {
          await route.fallback();
          return;
        }
        await route.fulfill({
          status: 401,
          contentType: "application/json",
          body: JSON.stringify({ error: "account_locked" }),
        });
      });

      await page.goto("/oidc/mfa-check?mfa_id=mfa-id-4&mfa_type=2", {
        waitUntil: "domcontentloaded",
      });

      await page.locator('input[maxlength="5"]').fill("12345");
      await page.getByRole("button", { name: /^verify$/i }).click();

      await expect(
        page.getByText(
          /your account is locked\. please contact support or reset your password\./i,
        ),
      ).toBeVisible({ timeout: 15_000 });

      await page.unroute("**/api/oidc/login").catch(() => {});
    });

    await test.step("[Positive] Successful verification with redirect_uri navigates to it", async () => {
      await page.route("**/api/oidc/login", async (route: any) => {
        const body = JSON.parse(route.request().postData() || "{}");
        if (!body.mfa_code) {
          await route.fallback();
          return;
        }
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            redirect_uri: "https://example.com/cb?code=abc",
          }),
        });
      });

      await page.goto("/oidc/mfa-check?mfa_id=mfa-id-5&mfa_type=2", {
        waitUntil: "domcontentloaded",
      });

      await page.locator('input[maxlength="5"]').fill("12345");
      await page.getByRole("button", { name: /^verify$/i }).click();

      // The page uses window.location.href (full-page redirect), so the URL
      // bar of the test browser reflects the redirect.
      await page.waitForURL(/example\.com/, { timeout: 15_000 });

      await page.unroute("**/api/oidc/login").catch(() => {});
    });
  });
});

test.describe("OIDC permission (consent) screen", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("Permission — greeting, Allow/Deny buttons, userName from query string", async ({
    page,
  }) => {
    await test.step("[Positive] Greeting + Allow + Deny buttons are present", async () => {
      await page.goto("/oidc/permission?client_id=test&redirect_uri=https%3A%2F%2Fexample.com%2Fcb", {
        waitUntil: "domcontentloaded",
      });

      await expect(page.getByRole("button", { name: /^allow$/i })).toBeVisible({
        timeout: 30_000,
      });
      await expect(page.getByRole("button", { name: /^deny$/i })).toBeVisible();
      await expect(page.getByText(/about to connect your blocks account/i)).toBeVisible();
    });

    await test.step("[Positive] userName from query string renders below the greeting", async () => {
      await page.goto(
        "/oidc/permission?userName=Jane+Doe&client_id=test&redirect_uri=https%3A%2F%2Fexample.com%2Fcb",
        { waitUntil: "domcontentloaded" },
      );

      await expect(page.getByText("Jane Doe")).toBeVisible({ timeout: 30_000 });
    });
  });
});

test.describe("OIDC error screen", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("OIDC error — generic Access Blocked, Sign In Failed with error_description, Back to Sign In", async ({
    page,
  }) => {
    await test.step("[Positive] Without query params, shows the generic 'Access Blocked' card", async () => {
      await page.goto("/oidc/error", { waitUntil: "domcontentloaded" });

      await expect(page.getByText(/access blocked/i)).toBeVisible({ timeout: 30_000 });
      await expect(
        page.getByText(/we couldn'?t sign you in at this time/i),
      ).toBeVisible();
      await expect(page.getByRole("button", { name: /back to sign in/i })).toBeVisible();
    });

    await test.step("[Positive] With error_description, shows the 'Sign In Failed' card with the message", async () => {
      await page.goto(
        "/oidc/error?error=signup_disabled_for_tenant&error_description=Signups+are+disabled+for+this+tenant",
        { waitUntil: "domcontentloaded" },
      );

      await expect(page.getByText(/sign in failed/i)).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/signups are disabled for this tenant/i)).toBeVisible();

      // Error code is humanized from snake_case.
      await expect(page.getByText("Signup Disabled For Tenant")).toBeVisible();
    });

    await test.step("[Positive] 'Back to Sign In' link returns to /oidc/login", async () => {
      await page.goto("/oidc/error", { waitUntil: "domcontentloaded" });

      const back = page.getByRole("button", { name: /back to sign in/i });
      await expect(back).toBeVisible({ timeout: 30_000 });
      await back.click();
      await expect(page).toHaveURL(/\/oidc\/login/, { timeout: 15_000 });
    });
  });
});
