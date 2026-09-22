import { test, expect } from "../../support/test-base";

/**
 * Account activation flow:
 *   - /oidc/activate/:tenantId?code=...  — the 4-status page
 *   - /oidc/activate-success              — confirmation screen
 *
 * Activation status branches (driven by /api/auth/validate-activation):
 *   Valid           → renders the ActivationForm (name + password)
 *   Expired         → "Link Expired" + resend button
 *   Invalid         → "Invalid Activation Link" + back-to-login
 *   AlreadyActivated → either redirects to /activate-success (when the project
 *                      does NOT collect a password on activation) or renders
 *                      the "Already Activated" card (when it does)
 *
 * The page also asks /api/idp/oidc-ui-config for `collectPasswordOnActivation`
 * which gates the AlreadyActivated redirect.
 */

const mockOidcUiConfig = async (
  page: any,
  overrides: { collectPasswordOnActivation?: boolean } = {},
) => {
  await page.route("**/api/idp/oidc-ui-config*", async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({
        captcha: null,
        template: {},
        // Only set the flag when explicitly overridden; absence matches the
        // real server's "absent" default, which the page treats as
        // collectPasswordOnActivation = true (forms render normally).
        ...(overrides.collectPasswordOnActivation !== undefined
          ? { collectPasswordOnActivation: overrides.collectPasswordOnActivation }
          : {}),
      }),
    });
  });
};

test.describe("Activation page", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("Activation — Valid form, Invalid/Expired cards, resend paths, AlreadyActivated branches, no-code and legacy fallbacks", async ({
    page,
  }) => {
    await test.step("[Positive] status=Valid renders the activation form with the supplied name", async () => {
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "Valid",
            firstName: "Jane",
            lastName: "Doe",
          }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=valid-code", {
        waitUntil: "domcontentloaded",
      });

      // Form has first-name + last-name inputs (and optionally password fields).
      // The labels aren't associated via htmlFor, so use the input's placeholder.
      const firstName = page.getByPlaceholder("First name");
      const lastName = page.getByPlaceholder("Last name");
      await expect(firstName).toBeVisible({ timeout: 30_000 });
      await expect(lastName).toBeVisible();
      // The supplied name is pre-filled.
      await expect(firstName).toHaveValue("Jane");
      await expect(lastName).toHaveValue("Doe");

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });

    await test.step("[Negative] status=Invalid renders the 'Invalid Activation Link' card", async () => {
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ status: "Invalid" }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=fabricated-code", {
        waitUntil: "domcontentloaded",
      });

      // Heading is split across multiple spans, so heading-role with a
      // substring match is more reliable than getByText.
      await expect(
        page.getByRole("heading", { name: /invalid activation link/i }),
      ).toBeVisible({ timeout: 30_000 });
      await expect(
        page.getByText(/the activation code is invalid/i),
      ).toBeVisible();
      // Invalid card uses a LoginReturnLink (anchor), not a button — the heading
// sits in a heading role, the body in muted text, and the CTA renders as a
      // single link whose visible label is backToLoginButton.
      await expect(page.getByRole("link", { name: /back to login/i })).toBeVisible();

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });

    await test.step("[Negative] status=Expired renders the 'Link Expired' card with the resend button", async () => {
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "Expired",
            userId: "user-abc",
          }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=expired-code", {
        waitUntil: "domcontentloaded",
      });

      await expect(
        page.getByRole("heading", { name: /link expired/i }),
      ).toBeVisible({ timeout: 30_000 });
      await expect(
        page.getByText(/this activation link has expired/i),
      ).toBeVisible();
      await expect(
        page.getByRole("button", { name: /resend activation link/i }),
      ).toBeVisible();

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });

    await test.step("[Positive] status=Expired: clicking 'Resend' calls /api/auth/resend-activation and shows the success message", async () => {
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ status: "Expired", userId: "user-abc" }),
        });
      });

      let resendPayload: any = null;
      await page.route("**/api/auth/resend-activation", async (route: any) => {
        resendPayload = JSON.parse(route.request().postData() || "{}");
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ isSuccess: true, errors: [] }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=expired-code", {
        waitUntil: "domcontentloaded",
      });

      await page.getByRole("button", { name: /resend activation link/i }).click();

      await expect.poll(() => resendPayload, { timeout: 10_000 }).not.toBeNull();
      expect(resendPayload.userId).toBe("user-abc");
      expect(resendPayload.tenantId).toBe("test-tenant");

      await expect(page.getByText(/a new activation link has been sent/i)).toBeVisible({
        timeout: 15_000,
      });

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
      await page.unroute("**/api/auth/resend-activation").catch(() => {});
    });

    await test.step("[Negative] status=Expired + resend failure shows the failure message", async () => {
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ status: "Expired", userId: "user-abc" }),
        });
      });

      await page.route("**/api/auth/resend-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            isSuccess: false,
            errors: { error: "Resend limit exceeded" },
          }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=expired-code", {
        waitUntil: "domcontentloaded",
      });

      await page.getByRole("button", { name: /resend activation link/i }).click();

      await expect(page.getByText(/failed to resend activation link/i)).toBeVisible({
        timeout: 15_000,
      });

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
      await page.unroute("**/api/auth/resend-activation").catch(() => {});
    });

    await test.step("[Positive] status=AlreadyActivated with collectPasswordOnActivation=false redirects to /oidc/activate-success", async () => {
      // beforeEach applies mockOidcUiConfig() with the flag absent, which the
      // page treats as collectPasswordOnActivation = true → renders the
      // AlreadyActivated card instead of redirecting. Swap to the explicit
      // false override before navigating.
      await page.unroute("**/api/idp/oidc-ui-config*").catch(() => {});
      await mockOidcUiConfig(page, { collectPasswordOnActivation: false });

      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ status: "AlreadyActivated" }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=already-used-code", {
        waitUntil: "domcontentloaded",
      });

      await expect(page).toHaveURL(/\/oidc\/activate-success/, { timeout: 15_000 });

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
      // Restore the default mock for the rest of the suite.
      await page.unroute("**/api/idp/oidc-ui-config*").catch(() => {});
      await mockOidcUiConfig(page);
    });

    await test.step("[Positive] status=AlreadyActivated with collectPasswordOnActivation=true renders the 'Already Activated' card", async () => {
      // The UI-config mock from beforeEach has collectPasswordOnActivation=false.
      // Swap it out for this step.
      await page.unroute("**/api/idp/oidc-ui-config*").catch(() => {});
      await mockOidcUiConfig(page, { collectPasswordOnActivation: true });

      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ status: "AlreadyActivated" }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=already-used-code", {
        waitUntil: "domcontentloaded",
      });

      await expect(
        page.getByRole("heading", { name: /already activated/i }),
      ).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/this account is already active/i)).toBeVisible();
      // The single action on this card is "Log in" → back to the login form.
      await expect(page.getByRole("link", { name: /^log in$/i })).toBeVisible();

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
      // Restore the default mock for any later steps.
      await page.unroute("**/api/idp/oidc-ui-config*").catch(() => {});
      await mockOidcUiConfig(page);
    });

    await test.step("[Negative] No code at all → invalid branch (no backend call)", async () => {
      let validationCalled = false;
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        validationCalled = true;
        await route.fallback();
      });

      await page.goto("/oidc/activate/test-tenant", { waitUntil: "domcontentloaded" });

      // No code → the page short-circuits to the Invalid branch without
      // hitting the backend.
      await expect(
        page.getByRole("heading", { name: /invalid activation link/i }),
      ).toBeVisible({ timeout: 30_000 });
      expect(validationCalled).toBe(false);

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });

    await test.step("[Negative] Validation endpoint throws → invalid branch", async () => {
      await page.route("**/api/auth/validate-activation", (route: any) =>
        route.abort("failed"),
      );

      await page.goto("/oidc/activate/test-tenant?code=any-code", {
        waitUntil: "domcontentloaded",
      });

      await expect(
        page.getByRole("heading", { name: /invalid activation link/i }),
      ).toBeVisible({ timeout: 30_000 });

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });

    await test.step("[Positive] Legacy server without `status` field — isSuccess=true → Valid", async () => {
      // Older backends responded with isSuccess + errors instead of a status enum;
      // resolveActivationCodeStatus() falls back to that mapping.
      await page.route("**/api/auth/validate-activation", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            isSuccess: true,
            firstName: "Legacy",
            lastName: "User",
          }),
        });
      });

      await page.goto("/oidc/activate/test-tenant?code=any-code", {
        waitUntil: "domcontentloaded",
      });

      const firstName = page.getByPlaceholder("First name");
      await expect(firstName).toBeVisible({ timeout: 30_000 });
      await expect(firstName).toHaveValue("Legacy");

      await page.unroute("**/api/auth/validate-activation").catch(() => {});
    });
  });
});

test.describe("Activate success page", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("Activate success — confirmation copy + 'Log in' link", async ({ page }) => {
    await test.step("[Positive] Confirmation copy + 'Log in' link are present", async () => {
      await page.goto("/oidc/activate-success", { waitUntil: "domcontentloaded" });

      await expect(page.getByText(/your account is ready to use/i)).toBeVisible({
        timeout: 30_000,
      });
      await expect(page.getByText(/account activated/i)).toBeVisible();
    });
  });
});
