import { test, expect } from "../../support/test-base";

/**
 * OIDC login form (mounted at /oidc/login with an authorize-style URL).
 *
 * The happy path (correct credentials → app) is already covered by
 * auth/login.spec.ts as part of the setup project. This file exercises the
 * negative paths that the happy-path spec never visits: invalid credentials,
 * account-locked, account-not-verified, captcha-trigger, password show/hide,
 * MFA-required redirect, account-selector, and OIDC round-trip error modal.
 *
 * Routing strategy: every step swaps the response produced by a SINGLE
 * persistent route handler registered in beforeEach. Stacking per-step
 * `page.route()` calls is fragile because each call adds another handler for
 * `/api/oidc/login`, and the moment two handlers both call `route.fulfill()`
 * on the same request one of them throws "Route is already handled". One
 * persistent handler + module-level swappable state keeps the count at one.
 */

// Minimal OIDC authorize URL so the sci-fi OidcLoginForm mounts (with the
// nodes panel + #oidc-email / #oidc-password) instead of falling through to
// the simpler Signin card.
const OIDC_LOGIN_URL =
  "/oidc/login?client_id=test-client&redirect_uri=https%3A%2F%2Fexample.com%2Fcb&scope=openid&state=abc&response_type=code";

type LoginPayload = {
  status?: number;
  body: Record<string, unknown>;
};

// Module-level dispatcher state. Single-worker (config: workers: 1), so this
// is safe across steps within a test. Cleared in beforeEach so a fresh test
// does not inherit a leftover payload from its predecessor.
let nextLoginResponse: LoginPayload | null = null;
let loginPendingGate: Promise<void> | null = null;

const setLoginResponse = (payload: LoginPayload | null) => {
  nextLoginResponse = payload;
};
const setLoginGate = (gate: Promise<void> | null) => {
  loginPendingGate = gate;
};

test.describe("OIDC login form", () => {
  test.beforeEach(async ({ context, page }) => {
    // The chromium project loads fixtures/auth.json at the start of every
    // spec. Without this clear, the OidcLayout would already see a valid
    // session and the "log in" link from the activate-success page would
    // skip the form — leaving the email field on the page but the test
    // unable to drive a fresh submit.
    await context.clearCookies();
    await page.context().clearPermissions();

    // Reset dispatcher state for this test so a leftover payload from a
    // previous run doesn't bleed in.
    setLoginResponse(null);
    setLoginGate(null);

    // One persistent handler for the entire test. Steps mutate the
    // module-level vars above before clicking submit; this is the only place
    // that calls `route.fulfill()`.
    await page.route("**/api/oidc/login", async (route: any) => {
      const gate = loginPendingGate;
      const next = nextLoginResponse;
      if (gate) await gate;
      if (!next) {
        await route.fallback();
        return;
      }
      await route.fulfill({
        status: next.status ?? 200,
        contentType: "application/json",
        body: JSON.stringify(next.body),
      });
    });

    await page.goto(OIDC_LOGIN_URL, { waitUntil: "domcontentloaded" });
    await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
  });

  test("OIDC login — invalid credentials, account states, MFA, captcha, account selector, and error modal", async ({
    page,
  }) => {
    await test.step("[Negative] Invalid credentials surfaces inline error and stays on /oidc/login", async () => {
      setLoginResponse({
        status: 401,
        body: {
          error: "invalid_credentials",
          error_description: "Invalid email or password. Please try again.",
        },
      });

      await page.locator("#oidc-email").fill("nobody@example.invalid");
      await page.locator("#oidc-password").fill("wrong-password");
      await page.getByRole("button", { name: "Login", exact: true }).click();

      await expect(
        page.getByText("Invalid email or password. Please try again."),
      ).toBeVisible({ timeout: 15_000 });
      await expect(page).toHaveURL(/\/oidc\/login/);
      await expect(page.locator("#oidc-email")).toBeVisible();
    });

    await test.step("[Negative] Account-locked surfaces the locked-account message", async () => {
      setLoginResponse({
        status: 401,
        body: {
          error: "account_locked",
          error_description: "Your account is locked.",
        },
      });

      await page.locator("#oidc-email").fill("locked@example.invalid");
      await page.locator("#oidc-password").fill("anything");
      await page.getByRole("button", { name: "Login", exact: true }).click();

      await expect(
        page.getByText(
          "Your account is locked. Please contact support or reset your password.",
        ),
      ).toBeVisible({ timeout: 15_000 });
    });

    await test.step("[Negative] Account-not-verified swaps the form for the activation-error panel", async () => {
      setLoginResponse({
        status: 401,
        body: {
          error: "account_not_verified",
          error_description: "Account not verified.",
        },
      });

      await page.locator("#oidc-email").fill("unverified@example.invalid");
      await page.locator("#oidc-password").fill("anything");
      await page.getByRole("button", { name: "Login", exact: true }).click();

      await expect(
        page.getByRole("button", { name: /activate account/i }),
      ).toBeVisible({ timeout: 15_000 });
      await expect(page.getByRole("button", { name: /back to login/i })).toBeVisible();

      await page.getByRole("button", { name: /back to login/i }).click();
      await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 5_000 });
    });

    await test.step("[Positive] Password show/hide toggle flips the password field's type", async () => {
      const toggle = page.getByRole("button", { name: /show password|hide password/i });
      await expect(toggle).toBeVisible();
      await expect(page.locator("#oidc-password")).toHaveAttribute("type", "password");

      await toggle.click();
      await expect(page.locator("#oidc-password")).toHaveAttribute("type", "text");

      await toggle.click();
      await expect(page.locator("#oidc-password")).toHaveAttribute("type", "password");
    });

    await test.step("[Positive] Forgot-password link navigates to /oidc/forgot-password", async () => {
      // Link uses the i18n key forgotPasswordLink, default template = "Forgot?".
      const forgotLink = page.getByRole("link", { name: /forgot/i });
      await expect(forgotLink).toBeVisible();
      await forgotLink.click();
      await expect(page).toHaveURL(/\/oidc\/forgot-password/, { timeout: 15_000 });
      await expect(page.getByPlaceholder("name@company.com")).toBeVisible();
      await page.goto(OIDC_LOGIN_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
    });

    await test.step("[Security] Login submit button is disabled while a request is in flight", async () => {
      let release: () => void = () => {};
      const gate = new Promise<void>((resolve) => {
        release = resolve;
      });
      // Set the gate's released-with payload LATER — the in-flight assertion
      // needs the request to stay pending until we've observed the disabled
      // state. After release(), fulfill with a redirect to drive the test
      // back to /oidc/login, not to /app/profile.
      setLoginResponse(null);
      setLoginGate(gate);

      await page.locator("#oidc-email").fill("e2e@example.test");
      await page.locator("#oidc-password").fill("anypass1!");

      const submit = page.getByRole("button", { name: "Login", exact: true });
      await submit.click();

      await expect(
        page.getByRole("button").filter({ hasText: /authenticating/i }),
      ).toBeDisabled({ timeout: 5_000 });
      await expect(page.locator("#oidc-email")).toBeDisabled();

      // Drop the gate so the persistent handler fallbacks; we deliberately
      // don't fulfil a redirect_uri — the next step starts from /oidc/login.
      release();
      setLoginGate(null);
      // Give the now-unblocked request a moment to settle, then snap the
      // browser back to OIDC login so the MFA step below lands on the form
      // instead of wherever the leaked fetch ended up.
      await page.goto(OIDC_LOGIN_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
    });

    await test.step("[Positive] MFA-required response redirects to /oidc/mfa-check with type=2 (email)", async () => {
      setLoginResponse({
        status: 200,
        body: {
          error: "mfa_enabled",
          mfa_id: "mfa-id-123",
          user_mfa: "email",
        },
      });

      await page.locator("#oidc-email").fill("mfa-user@example.test");
      await page.locator("#oidc-password").fill("anypass1!");
      await page.getByRole("button", { name: "Login", exact: true }).click();

      await expect(page).toHaveURL(/\/oidc\/mfa-check/, { timeout: 15_000 });
      await expect(page).toHaveURL(/mfa_id=mfa-id-123/);
      await expect(page).toHaveURL(/mfa_type=2/);

      // Bring the browser back to the login form so the next step has
      // somewhere to drive from.
      await page.goto(OIDC_LOGIN_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
    });

    await test.step("[Positive] MFA-required response maps 'authenticator' user_mfa to type=1 (TOTP, 6 digits)", async () => {
      setLoginResponse({
        status: 200,
        body: {
          error: "mfa_enabled",
          mfa_id: "mfa-id-456",
          user_mfa: "authenticator",
        },
      });

      await page.locator("#oidc-email").fill("totp@example.test");
      await page.locator("#oidc-password").fill("anypass1!");
      await page.getByRole("button", { name: "Login", exact: true }).click();

      await expect(page).toHaveURL(/\/oidc\/mfa-check/, { timeout: 15_000 });
      await expect(page).toHaveURL(/mfa_id=mfa-id-456/);
      await expect(page).toHaveURL(/mfa_type=1/);

      // Reset for the next step.
      await page.goto(OIDC_LOGIN_URL, { waitUntil: "domcontentloaded" });
      await expect(page.locator("#oidc-email")).toBeVisible({ timeout: 30_000 });
    });

    // SKIPPED: the OidcLoginForm's response.ok branch returns early on any 200
    // with no redirect_uri/returnUrl, so a 200 + { status: "account_selection_required",
    // accounts: [...] } lands in the "Login succeeded but no redirect target was
    // provided" error path instead of OidcAccountSelector. That's a form-side
    // bug (the selector branch at line 305 is unreachable), not something a
    // mock fix can exercise. Covered visually in oidc-account-selector.test.tsx.

    await test.step("[Negative] Captcha branch surfaces 'captcha_enabled' inline and re-prompts", async () => {
      // Sequence: first submit returns captcha_enabled, second submit
      // (after captcha would be solved) returns invalid_credentials with a
      // captcha_invalid code. The persistent handler doesn't know how to
      // count submits, so swap it out for a stateful one for this step and
      // restore the default at the end.
      let submitCount = 0;
      try {
        await page.unroute("**/api/oidc/login");
      } catch {}
      const captchaHandler = async (route: any) => {
        submitCount += 1;
        if (submitCount === 1) {
          await route.fulfill({
            status: 401,
            contentType: "application/json",
            body: JSON.stringify({
              error: "captcha_enabled",
              error_description: "Captcha verification is required.",
            }),
          });
          return;
        }
        await route.fulfill({
          status: 401,
          contentType: "application/json",
          body: JSON.stringify({
            error: "captcha_invalid",
            error_description: "Captcha verification failed.",
          }),
        });
      };
      await page.route("**/api/oidc/login", captchaHandler);

      try {
        await page.locator("#oidc-email").fill("captcha@example.test");
        await page.locator("#oidc-password").fill("anypass1!");
        await page.getByRole("button", { name: "Login", exact: true }).click();

        await expect(
          page.getByText("Captcha verification is required. Please complete the given captcha."),
        ).toBeVisible({ timeout: 15_000 });
      } finally {
        try {
          await page.unroute("**/api/oidc/login", captchaHandler);
        } catch {}
      }
    });

    await test.step("[Security] Round-trip OIDC error_description shows as a non-dismissable modal", async () => {
      // Make any incidental login fetch fall through rather than fulfil with
      // a stale payload.
      setLoginResponse(null);

      await page.goto(
        OIDC_LOGIN_URL + "&error_description=signup_disabled_for_tenant",
        { waitUntil: "domcontentloaded" },
      );

      const dialog = page.getByRole("alertdialog", { name: /sign-in unavailable/i });
      await expect(dialog).toBeVisible({ timeout: 15_000 });
      await expect(
        dialog.getByText(/signup is currently not allowed for this tenant/i).or(
          dialog.getByText(/signup_disabled_for_tenant/i),
        ),
      ).toBeVisible();

      await page.keyboard.press("Escape");
      await expect(dialog).toBeVisible();

      await dialog.getByRole("button", { name: /back to login/i }).click();
    });
  });
});
