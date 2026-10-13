import { request as playwrightRequest, type Page, type Route } from "@playwright/test";
import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";
import { e2eBaseUrl, e2eCredentials } from "../../support/env";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * Signup flow at /oidc/signup/:tenantId.
 *
 * The form is a stack of three backend-driven gates plus the form itself:
 *   1. /api/idp/oidc-ui-config — branding + copy + whether captcha is required
 *   2. /api/iam/signup-settings — isSignUpEnable / isEmailPasswordSignUpEnabled / isSSoSignUpEnabled
 *   3. /api/iam/organizations/config — collectOrganizationName flag, plus
 *      isSignupAutoJoinEnabled (blocks-iam#609), which with creation also on turns the
 *      organization-name field into a join-or-create choice
 *   4. /api/auth/login-options — only consulted when SSO is enabled; provides ssoInfo[]
 *
 * Each step stubs these so the form's branches can be driven deterministically.
 * The signup mutation hits /api/auth/signup and is mocked per-step.
 */

const SIGNUP_URL = "/oidc/signup/test-tenant";

// All four backend reads return defaults unless a test step overrides one of them.
const baseMocks = (overrides: {
  signupSetting?: unknown;
  orgConfig?: unknown;
  loginOptions?: unknown;
  oidcUiConfig?: unknown;
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
    // blocks-iam#609 H4: the anonymous config now always carries this flag (never the target id).
    isSignupAutoJoinEnabled: false,
  };
  const loginOptions = overrides.loginOptions ?? { ssoInfo: [] };
  const oidcUiConfig = overrides.oidcUiConfig ?? {
    captcha: null,
    template: {}, // server may return an empty object; normalize() fills defaults
  };

  return [
    // Tenant-scoped signup settings; tenantId comes from BLOCKS_X_BLOCKS_KEY or
    // an explicit query param.
    (route: Route) =>
      route.request().url().includes("/api/iam/signup-settings")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(signupSetting),
          })
        : route.fallback(),
    // Org config — only fetched when signup is enabled.
    (route: Route) =>
      route.request().url().includes("/api/iam/organizations/config")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(orgConfig),
          })
        : route.fallback(),
    // Login options (ssoInfo) — only when SSO signup is on.
    (route: Route) =>
      route.request().url().includes("/api/auth/login-options")
        ? route.fulfill({
            status: 200,
            contentType: "application/json",
            body: JSON.stringify(loginOptions),
          })
        : route.fallback(),
    // UI config / template.
    (route: Route) =>
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
  page: Page,
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
      await page.route("**/api/auth/signup", async (route: Route) => {
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
      await page.route("**/api/auth/signup", async (route: Route) => {
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
      await page.route("**/api/auth/signup", async (route: Route) => {
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
      await page.route("**/api/auth/signup", (route: Route) => route.abort("failed"));

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
      await page.route("**/api/auth/signup", async (route: Route) => {
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

  // ---------------------------------------------------------------------------------
  // blocks-iam#609: auto-join new sign-ups to a configured organization. The hosted page
  // only reads the anonymous config, so every state is driven by the mocked
  // /api/iam/organizations/config, and /api/auth/signup is mocked per step: nothing here
  // creates an account or touches tenant configuration.
  // ---------------------------------------------------------------------------------
  test("Signup — auto-join organization: join-or-create choice, payloads, unavailable target", async ({
    page,
  }) => {
    const LEGEND = "How would you like to start?";
    const JOIN = "Join this project's organization";
    const CREATE = "Create my own organization";
    const UNAVAILABLE = "This project's organization isn't available right now.";

    const autoJoinOrgConfig = (allowOrgCreationFromSignup: boolean) => ({
      allowOrgCreationFromSignup,
      isMultiOrgEnabled: true,
      isOrgNameUnique: false,
      isSignupAutoJoinEnabled: true,
    });

    const reload = async (orgConfig: unknown) => {
      await page.unroute("**/api/**");
      await applyBaseMocks(page, { orgConfig });
      await page.goto(SIGNUP_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#signup-first-name")).toBeVisible({ timeout: 30_000 });
    };

    const fillIdentity = async (email: string) => {
      await page.locator("#signup-first-name").fill("Jane");
      await page.locator("#signup-last-name").fill("Doe");
      await page.locator("#signup-email").fill(email);
      await page.locator("#signup-terms").check();
    };

    // Records every signup body and answers with the queued responses in order (the last repeats).
    const mockSignup = async (responses: { status: number; body: unknown }[]) => {
      const bodies: Record<string, unknown>[] = [];
      let call = 0;
      await page.route("**/api/auth/signup", async (route: Route) => {
        bodies.push(route.request().postDataJSON() as Record<string, unknown>);
        const next = responses[Math.min(call, responses.length - 1)];
        call += 1;
        await route.fulfill({
          status: next.status,
          contentType: "application/json",
          body: JSON.stringify(next.body),
        });
      });
      return bodies;
    };
    const ok = { status: 200, body: { isSuccess: true, errors: null } };
    const unavailable = {
      status: 400,
      body: { isSuccess: false, errors: { signup_organization_unavailable: UNAVAILABLE } },
    };

    const nameField = page.locator("#signup-organization-name");
    const joinRadio = page.getByRole("radio", { name: JOIN });
    const createRadio = page.getByRole("radio", { name: CREATE });
    const submit = page.getByRole("button", { name: /create account/i });

    await test.step("[Positive] H15: auto-join on, creation off renders no organization UI", async () => {
      await reload(autoJoinOrgConfig(false));
      await expect(page.getByRole("radio")).toHaveCount(0);
      await expect(nameField).toHaveCount(0);
    });

    await test.step("[Positive] H12: auto-join and creation on show the choice with Join preselected and no name field", async () => {
      await reload(autoJoinOrgConfig(true));
      await expect(page.getByRole("group", { name: LEGEND })).toBeVisible();
      await expect(joinRadio).toBeChecked();
      await expect(createRadio).not.toBeChecked();
      await expect(nameField).toHaveCount(0);
    });

    await test.step("[Positive] H13: Create reveals the required name field; Join hides it and drops its value", async () => {
      await fillIdentity("jane@example.test");
      await createRadio.check();
      await expect(nameField).toBeVisible();
      // Required: submit stays disabled until a name is typed.
      await expect(submit).toBeDisabled();
      await nameField.fill("Acme");
      await expect(submit).toBeEnabled();

      await joinRadio.check();
      await expect(nameField).toHaveCount(0);
      await expect(submit).toBeEnabled();

      await createRadio.check();
      await expect(nameField).toHaveValue("");
      await expect(nameField).toHaveAttribute("aria-invalid", "false");
    });

    await test.step("[A11y] S4: arrow keys move between the radios", async () => {
      await reload(autoJoinOrgConfig(true));
      await joinRadio.focus();
      await page.keyboard.press("ArrowDown");
      await expect(createRadio).toBeChecked();
      await expect(nameField).toBeVisible();
      await page.keyboard.press("ArrowUp");
      await expect(joinRadio).toBeChecked();
      await expect(nameField).toHaveCount(0);
    });

    await test.step("[Positive] H14: Join submits without a creation flag or name", async () => {
      await reload(autoJoinOrgConfig(true));
      const bodies = await mockSignup([ok]);
      await fillIdentity("join@example.test");
      await submit.click();

      await expect(page).toHaveURL(/\/oidc\/signup-email-sent/, { timeout: 15_000 });
      expect(bodies).toHaveLength(1);
      expect(bodies[0]).not.toHaveProperty("createOrganizationDuringSignup");
      expect(bodies[0]).not.toHaveProperty("organizationName");
      expect(bodies[0]).not.toHaveProperty("organizationChoice");
      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Positive] H14: Create submits the creation flag with the name", async () => {
      await reload(autoJoinOrgConfig(true));
      const bodies = await mockSignup([ok]);
      await fillIdentity("create@example.test");
      await createRadio.check();
      await nameField.fill("AJ Org 2");
      await submit.click();

      await expect(page).toHaveURL(/\/oidc\/signup-email-sent/, { timeout: 15_000 });
      expect(bodies[0]).toEqual(
        expect.objectContaining({ createOrganizationDuringSignup: true, organizationName: "AJ Org 2" }),
      );
      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Negative] C7: unavailable organization is announced, switches to Create and focuses the name; a named resubmit succeeds", async () => {
      await reload(autoJoinOrgConfig(true));
      const bodies = await mockSignup([unavailable, ok]);
      await fillIdentity("aj-3@example.test");
      await submit.click();

      await expect(page.getByRole("alert")).toContainText(UNAVAILABLE, { timeout: 15_000 });
      await expect(createRadio).toBeChecked();
      await expect(nameField).toBeFocused();
      await expect(page).toHaveURL(/\/oidc\/signup/);

      await nameField.fill("AJ Org 3");
      await submit.click();
      await expect(page).toHaveURL(/\/oidc\/signup-email-sent/, { timeout: 15_000 });
      expect(bodies).toHaveLength(2);
      expect(bodies[0]).not.toHaveProperty("createOrganizationDuringSignup");
      expect(bodies[1]).toEqual(
        expect.objectContaining({ createOrganizationDuringSignup: true, organizationName: "AJ Org 3" }),
      );
      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Negative] S5: with creation off the unavailable message shows and no organization UI appears", async () => {
      await reload(autoJoinOrgConfig(false));
      await mockSignup([unavailable]);
      await fillIdentity("aj-4@example.test");
      await submit.click();

      await expect(page.getByRole("alert")).toContainText(UNAVAILABLE, { timeout: 15_000 });
      await expect(page.getByRole("radio")).toHaveCount(0);
      await expect(nameField).toHaveCount(0);
      await page.unroute("**/api/auth/signup").catch(() => {});
    });

    await test.step("[Positive] H16: auto-join off, creation on keeps today's organization-name field", async () => {
      await reload({
        allowOrgCreationFromSignup: true,
        isMultiOrgEnabled: true,
        isOrgNameUnique: false,
        isSignupAutoJoinEnabled: false,
      });
      await expect(nameField).toBeVisible();
      await expect(page.getByRole("radio")).toHaveCount(0);
    });

    await test.step("[Positive] C12: multi-org off ignores a stored auto-join flag", async () => {
      await reload({
        allowOrgCreationFromSignup: true,
        isMultiOrgEnabled: false,
        isOrgNameUnique: false,
        isSignupAutoJoinEnabled: true,
      });
      await expect(page.getByRole("radio")).toHaveCount(0);
      await expect(nameField).toHaveCount(0);
    });
  });
});

// -------------------------------------------------------------------------------------
// blocks-iam#609 against the deployed preview API (spec §10 steps 1, 2 and 8).
//
// Follows the suite's live-API convention (tests/iam/*): signed in as the shared E2E account,
// calls through iamApi. Every save re-sends the stored creation flags, which that endpoint
// does not treat as optional, and never touches multi-org (consent cannot be withdrawn).
// -------------------------------------------------------------------------------------

type OrgConfig = {
  allowOrgCreationFromCloud: boolean;
  allowOrgCreationFromConstruct: boolean;
  allowOrgCreationFromSignup: boolean;
  allowOrgCreationFromPortal: boolean;
  isMultiOrgEnabled: boolean;
  consentForMultiOrgEnable: boolean;
  isSignupAutoJoinEnabled?: boolean;
  signupAutoJoinOrganizationId?: string | null;
};
type SaveResult = { isSuccess?: boolean; errors?: Record<string, string> | null };

const CONFIG_PATH = "/api/iam/organizations/config";
const AUTO_JOIN_LIVE = process.env.E2E_AUTOJOIN_LIVE === "1";

const readAdminConfig = async (page: Page) => {
  const res = await iamApi<OrgConfig>(page, "GET", `${CONFIG_PATH}/admin`);
  expect(res.status).toBe(200);
  return res.json;
};

// Only the stored creation flags are carried over; the auto-join pair is sent only when given,
// so a call without it exercises the "older client" path (C3).
const saveConfig = (page: Page, stored: OrgConfig, autoJoin: Record<string, unknown> = {}) =>
  iamApi<SaveResult>(page, "POST", CONFIG_PATH, {
    allowOrgCreationFromCloud: stored.allowOrgCreationFromCloud,
    allowOrgCreationFromConstruct: stored.allowOrgCreationFromConstruct,
    allowOrgCreationFromSignup: stored.allowOrgCreationFromSignup,
    allowOrgCreationFromPortal: stored.allowOrgCreationFromPortal,
    isMultiOrgEnabled: stored.isMultiOrgEnabled,
    consentForMultiOrgEnable: stored.consentForMultiOrgEnable,
    ...autoJoin,
  });

// Anonymous calls go through a fresh request context: iamApi carries the signed-in session, and a
// sign-up made with a caller's token is not the anonymous sign-up the hosted page performs.
const anonymousApi = async <T = Record<string, unknown>>(
  method: "GET" | "POST",
  path: string,
  tenant: string,
  body?: unknown,
) => {
  const context = await playwrightRequest.newContext({ baseURL: e2eBaseUrl(), ignoreHTTPSErrors: true });
  try {
    const res = await context.fetch(path, {
      method,
      headers: { "X-Blocks-Key": tenant, Accept: "application/json" },
      ...(body === undefined ? {} : { data: body }),
    });
    const raw = await res.text();
    let json = {} as T;
    try {
      json = (raw ? JSON.parse(raw) : {}) as T;
    } catch {
      // Non-JSON body: assertions on `raw` still apply.
    }
    return { status: res.status(), json, raw };
  } finally {
    await context.dispose();
  }
};

const autoJoinState = (config: OrgConfig) => ({
  isSignupAutoJoinEnabled: config.isSignupAutoJoinEnabled ?? false,
  signupAutoJoinOrganizationId: config.signupAutoJoinOrganizationId ?? null,
  allowOrgCreationFromCloud: config.allowOrgCreationFromCloud,
  allowOrgCreationFromConstruct: config.allowOrgCreationFromConstruct,
  allowOrgCreationFromSignup: config.allowOrgCreationFromSignup,
  allowOrgCreationFromPortal: config.allowOrgCreationFromPortal,
});

const saveWithRetry = async (page: Page, stored: OrgConfig, autoJoin: Record<string, unknown>) => {
  const first = await saveConfig(page, stored, autoJoin);
  if (first.status === 200 && first.json.isSuccess === true) return first;
  return saveConfig(page, stored, autoJoin);
};

// Restores the auto-join pair in an order that can never leave auto-join on by mistake:
//   1. switch it off (no target sent, so nothing is validated and the save cannot be rejected);
//   2. re-send the target only when it differs from the original (an unchanged target is never
//      re-sent, the C4 path, so an organization disabled since is not re-validated);
//   3. switch it back on last, and only if it was on.
// Each save is retried once, and the result is re-read and compared with the original.
const restoreAutoJoin = async (page: Page, original: OrgConfig) => {
  const off = await saveWithRetry(page, original, { isSignupAutoJoinEnabled: false });
  expect(off.json.isSuccess, "restore: auto-join switched off").toBe(true);

  const originalTarget = original.signupAutoJoinOrganizationId ?? null;
  const current = await readAdminConfig(page);
  if ((current.signupAutoJoinOrganizationId ?? null) !== originalTarget) {
    // A blank target is stored as "none"; null would mean "keep the current one".
    const target = await saveWithRetry(page, original, { signupAutoJoinOrganizationId: originalTarget ?? "" });
    expect(target.json.isSuccess, "restore: original target").toBe(true);
  }

  if (original.isSignupAutoJoinEnabled) {
    const on = await saveWithRetry(page, original, { isSignupAutoJoinEnabled: true });
    expect(on.json.isSuccess, "restore: auto-join switched back on").toBe(true);
  }

  expect(autoJoinState(await readAdminConfig(page)), "restore: config matches the original").toEqual(
    autoJoinState(original),
  );
};

// Runs by default: reads, plus saves the server must reject, which leave stored state unchanged.
// The rejected saves run only on a multi-org tenant, the only state in which they are validated;
// with multi-org off the same payloads would be stored as sent (D9).
test.describe("Signup auto-join — organization config API, read-only (blocks-iam#609)", () => {
  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
  });

  test("H3 admin read, C5 anonymous read, C1–C2 rejected saves leave config unchanged", async ({ page }) => {
    test.setTimeout(90_000);
    const tenant = await resolveTenantId(page);
    const original = await readAdminConfig(page);
    let changed: boolean | undefined;

    try {
      await test.step("[Positive] H3: the admin read carries both auto-join fields", async () => {
        expect(original).toHaveProperty("isSignupAutoJoinEnabled");
        expect(original).toHaveProperty("signupAutoJoinOrganizationId");
      });

      await test.step("[Security] H4/C5: the anonymous read has the flag but never the target", async () => {
        const anonymous = await anonymousApi("GET", CONFIG_PATH, tenant);
        expect(anonymous.status).toBe(200);
        expect(typeof anonymous.json.isSignupAutoJoinEnabled).toBe("boolean");
        expect(anonymous.json).not.toHaveProperty("signupAutoJoinOrganizationId");
        expect(anonymous.raw).not.toContain("signupAutoJoinOrganizationId");
      });

      if (!original.isMultiOrgEnabled) {
        test.info().annotations.push({
          type: "skipped-step",
          description: "C1/C2: tenant is single-org, where these payloads would be stored rather than rejected",
        });
        return;
      }

      await test.step("[Negative] C1: auto-join on with a blank target is rejected", async () => {
        const rejected = await saveConfig(page, original, {
          isSignupAutoJoinEnabled: true,
          signupAutoJoinOrganizationId: "",
        });
        expect(rejected.json.isSuccess).toBe(false);
        expect(rejected.json.errors).toHaveProperty("signup_auto_join_organization_required");
      });

      await test.step("[Negative] C2: a target that does not exist is rejected", async () => {
        const rejected = await saveConfig(page, original, {
          isSignupAutoJoinEnabled: true,
          signupAutoJoinOrganizationId: `e2e-missing-${Date.now()}`,
        });
        expect(rejected.json.isSuccess).toBe(false);
        expect(rejected.json.errors).toHaveProperty("signup_organization_unavailable");
      });
    } finally {
      // A rejected save changes nothing (C1/C2). Should one ever be stored, put it back, then fail.
      changed =
        JSON.stringify(autoJoinState(await readAdminConfig(page))) !== JSON.stringify(autoJoinState(original));
      if (changed) {
        await restoreAutoJoin(page, original);
      }
    }
    expect(changed, "rejected saves left the stored config unchanged").toBe(false);
  });
});

// Opt-in (E2E_AUTOJOIN_LIVE=1): turns auto-join on for the tenant for the length of the test,
// then restores it. Creates no account and no organization.
test.describe("Signup auto-join — organization config API, mutating (blocks-iam#609, opt-in)", () => {
  test.skip(!AUTO_JOIN_LIVE, "changes tenant config; set E2E_AUTOJOIN_LIVE=1 on a dedicated tenant");

  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
  });

  test("H1–H4 save and read, C3 older-client save, C10 existing account", async ({ page }) => {
    test.setTimeout(120_000);
    const tenant = await resolveTenantId(page);
    const original = await readAdminConfig(page);

    try {
      await test.step("[Positive] H2/H3: auto-join to \"default\" is saved and read back", async () => {
        const saved = await saveConfig(page, original, {
          isSignupAutoJoinEnabled: true,
          signupAutoJoinOrganizationId: "default",
        });
        expect(saved.json.isSuccess).toBe(true);
        const read = await readAdminConfig(page);
        expect(read.isSignupAutoJoinEnabled).toBe(true);
        expect(read.signupAutoJoinOrganizationId).toBe("default");
      });

      await test.step("[Security] H4/C5: with auto-join on, the anonymous read has the flag but never the target", async () => {
        const anonymous = await anonymousApi("GET", CONFIG_PATH, tenant);
        expect(anonymous.status).toBe(200);
        expect(anonymous.json.isSignupAutoJoinEnabled).toBe(true);
        expect(anonymous.raw).not.toContain("signupAutoJoinOrganizationId");
      });

      await test.step("[Positive] C3: a save without the new fields keeps auto-join as it was", async () => {
        const saved = await saveConfig(page, original);
        expect(saved.json.isSuccess).toBe(true);
        const read = await readAdminConfig(page);
        expect(read.isSignupAutoJoinEnabled).toBe(true);
        expect(read.signupAutoJoinOrganizationId).toBe("default");
      });

      await test.step("[Positive] H1: an enabled organization can be the target", async () => {
        const listed = await iamApi<{ organizations?: { itemId: string; isDisabled?: boolean }[] }>(
          page,
          "GET",
          "/api/iam/organizations?page=0&pageSize=50",
        );
        const enabled = (listed.json.organizations ?? []).find((o) => !o.isDisabled);
        if (listed.status !== 200 || !enabled) {
          test.info().annotations.push({ type: "skipped-step", description: "H1: tenant has no enabled organization" });
          return;
        }
        const saved = await saveConfig(page, original, {
          isSignupAutoJoinEnabled: true,
          signupAutoJoinOrganizationId: enabled.itemId,
        });
        expect(saved.json.isSuccess).toBe(true);
        expect((await readAdminConfig(page)).signupAutoJoinOrganizationId).toBe(enabled.itemId);
      });

      await test.step("[Negative] C10: signing up an existing active email changes none of its memberships", async () => {
        const before = await iamApi(page, "GET", "/api/iam/organizations/my");
        const signup = await anonymousApi<SaveResult>("POST", "/api/auth/signup", tenant, {
          email: e2eCredentials().email,
          firstName: "E2E",
          lastName: "Existing",
        });
        const errors = signup.json.errors ?? {};
        if ("CaptchaCode" in errors || "signup_disabled" in errors) {
          test.info().annotations.push({
            type: "skipped-step",
            description: "C10: email sign-up is off or CAPTCHA-protected on this tenant",
          });
          return;
        }
        expect(signup.status).toBe(400);
        expect(errors).toHaveProperty("already_signed_up");
        const after = await iamApi(page, "GET", "/api/iam/organizations/my");
        expect(after.raw).toBe(before.raw);
      });
    } finally {
      await restoreAutoJoin(page, original);
    }
  });
});

// -------------------------------------------------------------------------------------
// blocks-iam#609 sign-ups that create real accounts (spec §10 steps 3, 4 and 7). Opt-in only:
// IAM has no HTTP endpoint that deletes a user or an organization, so every run leaves pending
// users (and, for H7, one organization) behind on the target tenant. Run it only against a dedicated tenant (T1 in the spec), with
//   E2E_AUTOJOIN_LIVE=1
//   E2E_AUTOJOIN_ORG_ID=<an enabled organization on that tenant>
//   E2E_AUTOJOIN_EMAIL_DOMAIN=<a test mail domain>
// and email sign-up on, multi-org on and CAPTCHA off (or bypassed) for that tenant.
// -------------------------------------------------------------------------------------
test.describe("Signup auto-join — account placement (blocks-iam#609, opt-in)", () => {
  test.skip(process.env.E2E_AUTOJOIN_LIVE !== "1", "creates real accounts; set E2E_AUTOJOIN_LIVE=1 on a dedicated tenant");

  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
  });

  test("H5 joins the target, H7 creates only its own organization, H8 gets no organization", async ({ page }) => {
    test.setTimeout(120_000);
    const targetId = process.env.E2E_AUTOJOIN_ORG_ID ?? "";
    const domain = process.env.E2E_AUTOJOIN_EMAIL_DOMAIN ?? "";
    expect(targetId, "E2E_AUTOJOIN_ORG_ID").not.toBe("");
    expect(domain, "E2E_AUTOJOIN_EMAIL_DOMAIN").not.toBe("");
    const tenant = await resolveTenantId(page);
    const original = await readAdminConfig(page);
    const stamp = Date.now();

    const signup = (email: string, extra: Record<string, unknown> = {}) =>
      anonymousApi<{ isSuccess?: boolean; itemId?: string; organizationId?: string | null }>(
        "POST",
        "/api/auth/signup",
        tenant,
        { email, firstName: "E2E", lastName: "AutoJoin", ...extra },
      );
    const storedOrganizations = async (userId: string) => {
      const user = await iamApi<{ data?: Record<string, unknown> }>(page, "GET", `/api/iam/users/${userId}`);
      expect(user.status).toBe(200);
      return (user.json.data?.organizationIds ?? user.json.data?.OrganizationIds) as string[] | undefined;
    };

    try {
      await test.step("[Positive] H5: auto-join on, no create: the user is stored in the target", async () => {
        expect(
          (await saveConfig(page, original, { isSignupAutoJoinEnabled: true, signupAutoJoinOrganizationId: targetId }))
            .json.isSuccess,
        ).toBe(true);
        const res = await signup(`e2e-aj-${stamp}-1@${domain}`);
        expect(res.json.isSuccess).toBe(true);
        expect(res.json.organizationId).toBe(targetId);
        expect(await storedOrganizations(res.json.itemId!)).toEqual([targetId]);
      });

      await test.step("[Positive] H7: create chosen: only the new organization, never the target", async () => {
        if (!original.allowOrgCreationFromSignup) {
          test.info().annotations.push({ type: "skipped-step", description: "H7: creation from sign-up is off" });
          return;
        }
        const res = await signup(`e2e-aj-${stamp}-2@${domain}`, {
          createOrganizationDuringSignup: true,
          organizationName: `AJ Org ${stamp}`,
        });
        expect(res.json.isSuccess).toBe(true);
        expect(res.json.organizationId).toBeTruthy();
        expect(res.json.organizationId).not.toBe(targetId);
        expect(await storedOrganizations(res.json.itemId!)).toEqual([res.json.organizationId]);
      });

      await test.step("[Positive] H8: auto-join off, no create: the user has no organization", async () => {
        expect((await saveConfig(page, original, { isSignupAutoJoinEnabled: false })).json.isSuccess).toBe(true);
        const res = await signup(`e2e-aj-${stamp}-4@${domain}`);
        expect(res.json.isSuccess).toBe(true);
        expect(res.json.organizationId ?? null).toBeNull();
        expect(await storedOrganizations(res.json.itemId!)).toEqual([]);
      });
    } finally {
      await restoreAutoJoin(page, original);
    }
  });
});
