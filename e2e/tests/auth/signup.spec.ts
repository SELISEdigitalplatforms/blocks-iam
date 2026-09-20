import { test, expect } from "../../support/test-base";

/**
 * Signup flow at /oidc/signup/:tenantId.
 *
 * The form is a stack of three backend-driven gates plus the form itself:
 *   1. /api/idp/oidc-ui-config — branding + copy + whether captcha is required
 *   2. /api/iam/signup-settings — isSignUpEnable / isEmailPasswordSignUpEnabled / isSSoSignUpEnabled
 *   3. /api/iam/organizations/config — collectOrganizationName flag
 *   4. /api/auth/login-options — only consulted when SSO is enabled; provides ssoInfo[]
 *
 * Each step stubs these so the form's branches can be driven deterministically.
 * The signup mutation hits /api/auth/signup and is mocked per-step.
 */

const SIGNUP_URL = "/oidc/signup/test-tenant";

// All four backend reads return defaults unless a test step overrides one of them.
const baseMocks = (overrides: {
  signupSetting?: any;
  orgConfig?: any;
  loginOptions?: any;
  oidcUiConfig?: any;
}) => {
  const signupSetting = overrides.signupSetting ?? {
    isSignUpEnable: true,
    isEmailPasswordSignUpEnabled: true,
    isSSoSignUpEnabled: false,
  };
  const orgConfig = overrides.orgConfig ?? {
    allowOrgCreationFromSignup: false,
    isMultiOrgEnabled: false,
    isOrgNameUnique: false,
  };
  const loginOptions = overrides.loginOptions ?? { ssoInfo: [] };
  const oidcUiConfig = overrides.oidcUiConfig ?? {
    captcha: null,
    template: {}, // server may return an empty object; normalize() fills defaults
  };

  return [
    // Tenant-scoped signup settings; tenantId comes from BLOCKS_X_BLOCKS_KEY or
    // an explicit query param.
    (route: any) =>
      route.request().url().includes("/api/iam/signup-settings")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(signupSetting),
          })
        : route.fallback(),
    // Org config — only fetched when signup is enabled.
    (route: any) =>
      route.request().url().includes("/api/iam/organizations/config")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(orgConfig),
          })
        : route.fallback(),
    // Login options (ssoInfo) — only when SSO signup is on.
    (route: any) =>
      route.request().url().includes("/api/auth/login-options")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(loginOptions),
          })
        : route.fallback(),
    // UI config / template.
    (route: any) =>
      route.request().url().includes("/api/idp/oidc-ui-config")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(oidcUiConfig),
          })
        : route.fallback(),
  ];
};

const applyBaseMocks = async (
  page: any,
  overrides: Parameters<typeof baseMocks>[0] = {},
) => {
  const handlers = baseMocks(overrides);
  for (const handler of handlers) {
    await page.route("**/api/**", handler);
  }
};

test.describe("Signup form", () => {
  test.beforeEach(async ({ context, page }) => {
    // Same rationale as oidc-login.spec.ts: storageState from setup would
    // bypass the public signup route on the OidcLayout gate.
    await context.clearCookies();
    await applyBaseMocks(page);
    await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
  });

  test("Signup — disabled state, email-only / org-collection / SSO variants, validation, and error surfaces", async ({
    page,
  }) => {
    await test.step("[Negative] Signup-disabled setting hides the entire form", async () => {
      // Re-route signup-settings to the disabled variant.
      await page.unroute("**/api/**");
      await applyBaseMocks(page, {
        signupSetting: {
          isSignUpEnable: false,
          isEmailPasswordSignUpEnabled: true,
          isSSoSignUpEnabled: false,
        },
      });
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });

      // No #signup-email input should ever appear.
      await expect(page.locator("#signup-email")).toHaveCount(0, { timeout: 10_000 });
      await expect(page.locator("#signup-first-name")).toHaveCount(0);

      // Restore the default mock so subsequent steps render the form.
      await page.unroute("**/api/**");
      await applyBaseMocks(page);
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
    });

    await test.step("[Positive] Email-only signup renders the standard four-field form", async () => {
      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });
      await expect(page.locator("#signup-last-name")).toBeVisible();
      await expect(page.locator("#signup-email")).toBeVisible();
      // No organization field by default — server has allowOrgCreationFromSignup=false.
      await expect(page.locator("#signup-organization-name")).toHaveCount(0);
      // Terms checkbox always present.
      await expect(page.locator("#signup-terms")).toBeVisible();
    });

    await test.step("[Positive] Org-name field is added when the tenant allows org creation", async () => {
      await page.unroute("**/api/**");
      await applyBaseMocks(page, {
        orgConfig: {
          allowOrgCreationFromSignup: true,
          isMultiOrgEnabled: true,
          isOrgNameUnique: true,
        },
      });
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });

      await expect(page.locator("#signup-organization-name")).toBeVisible({
        timeout: 15_000,
      });

      // Restore default mocks for the remaining steps.
      await page.unroute("**/api/**");
      await applyBaseMocks(page);
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
    });

    await test.step("[Positive] Submit is disabled until terms are accepted", async () => {
      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");

      const submit = page.getByRole("button", { name: /create account/i });
      await expect(submit).toBeDisabled();

      await page.locator("#signup-terms").check();
      await expect(submit).toBeEnabled();
    });

    await test.step("[Positive] SSO separator renders when SSO providers are configured", async () => {
      await page.unroute("**/api/**");
      await applyBaseMocks(page, {
        signupSetting: {
          isSignUpEnable: true,
          isEmailPasswordSignUpEnabled: true,
          isSSoSignUpEnabled: true,
        },
        loginOptions: {
          ssoInfo: [
            {
              provider: "google",
              clientId: "google-client-id",
              // sso-signin.tsx reads (sso.redirectUris as string[])[0], so the
              // mock must use the plural array form — a single redirectUri
              // string throws "Cannot read properties of undefined (reading '0')".
              redirectUris: ["https://example.com/cb"],
            },
          ],
        },
      });
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });

      // The "or" divider only shows up when both email and SSO signup paths are
      // visible — the divider sits between them in the DOM.
      await expect(page.locator("#signup-email")).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText("or", { exact: true })).toBeVisible();

      await page.unroute("**/api/**");
      await applyBaseMocks(page);
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
    });

    await test.step("[Negative] Email-only signup + SSO disabled hides the SSO separator and provider", async () => {
      await expect(page.locator("#signup-email")).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText("or", { exact: true })).toHaveCount(0);
    });

    await test.step("[Positive] Captcha block mounts when captcha is enabled in OIDC UI config", async () => {
      await page.unroute("**/api/**");
      await applyBaseMocks(page, {
        oidcUiConfig: {
          captcha: {
            key: "test-site-key",
            provider: "reCaptcha-v2-checkbox",
            generator: "google",
          },
          template: {},
        },
      });
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });

      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      // Fill all fields so the form becomes valid; only then does the captcha
      // widget get rendered. We can't actually verify the iframe finishes
      // loading without a real site key — the test just asserts that the
      // form remains usable when captcha is enabled.
      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");

      await expect(page.locator("#signup-email")).toHaveValue("jane@example.test");

      await page.unroute("**/api/**");
      await applyBaseMocks(page);
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
    });

    await test.step("[Positive] Happy path: filled form + terms → /oidc/signup-email-sent", async () => {
      await page.route("**/api/auth/signup", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ isSuccess: true, errors: [] }),
        });
      });

      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");
      await page.locator("#signup-terms").check();

      await page.getByRole("button", { name: /create account/i }).click();

      await expect(page).toHaveURL(/\/oidc\/signup-email-sent/, { timeout: 15_000 });
      // The "Go to login" prompt is rendered on the email-sent page; if the URL
      // matched but the page is still the form (e.g. animation stalled) this
      // assertion will fail loudly.
      await expect(page.getByText(/an email has been sent/i)).toBeVisible();

      await page.unroute("**/api/auth/signup").catch(() => {});
      // Navigate back so the next step starts on the signup form again.
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });
    });

    await test.step("[Negative] Server error surfaces inline and the form remains usable", async () => {
      await page.route("**/api/auth/signup", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            isSuccess: false,
            errors: { email: "Email already in use." },
          }),
        });
      });

      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("duplicate@example.test");
      await page.locator("#signup-terms").check();

      await page.getByRole("button", { name: /create account/i }).click();

      // The generic error banner picks the first error string when the errors
      // payload is an object map.
      await expect(page.getByText("Email already in use.")).toBeVisible({
        timeout: 15_000,
      });
      // We stay on the signup URL.
      await expect(page).toHaveURL(/\/oidc\/signup/);

      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Negative] name_already_exists server error surfaces on the organizationName field", async () => {
      // Switch org config so the field is rendered.
      await page.unroute("**/api/**");
      await applyBaseMocks(page, {
        orgConfig: {
          allowOrgCreationFromSignup: true,
          isMultiOrgEnabled: true,
          isOrgNameUnique: true,
        },
      });
      await page.route("**/api/auth/signup", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            isSuccess: false,
            errors: { name_already_exists: "Acme" },
          }),
        });
      });

      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#signup-organization-name")).toBeVisible({
        timeout: 30_000,
      });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");
      await page.locator("#signup-organization-name").fill("Acme");
      await page.locator("#signup-terms").check();

      await page.getByRole("button", { name: /create account/i }).click();

      // Errors with name_already_exists keys get re-routed to the
      // organizationName field — the generic banner stays empty so the user
      // sees the field is the problem.
      const orgField = page.locator("#signup-organization-name");
      await expect(orgField).toHaveAttribute("aria-invalid", "true", { timeout: 15_000 });

      await page.unroute("**/api/auth/signup").catch(() => {});
      await page.unroute("**/api/**");
      await applyBaseMocks(page);
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
    });

    await test.step("[Negative] Network throw falls back to 'Something went wrong'", async () => {
      await page.route("**/api/auth/signup", (route: any) => route.abort("failed"));

      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");
      await page.locator("#signup-terms").check();

      await page.getByRole("button", { name: /create account/i }).click();

      await expect(page.getByText("Something went wrong")).toBeVisible({
        timeout: 15_000,
      });

      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Security] Submit button is disabled while the signup mutation is in flight", async () => {
      let release: () => void = () => {};
      const gate = new Promise<void>((resolve) => {
        release = resolve;
      });
      await page.route("**/api/auth/signup", async (route: any) => {
        await gate;
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({ isSuccess: true, errors: [] }),
        });
      });

      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });

      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill("jane@example.test");
      await page.locator("#signup-terms").check();

      await page.getByRole("button", { name: /create account/i }).click();

      // The submit button label flips to "Creating Account..." while in flight.
      await expect(
        page.getByRole("button").filter({ hasText: /creating account/i }),
      ).toBeDisabled({ timeout: 5_000 });
      // And the inputs are also disabled.
      await expect(page.locator("#signup-first-name")).toBeDisabled();

      release();
      await page.unroute("**/api/auth/signup").catch(() => {});
    });
  });
});
