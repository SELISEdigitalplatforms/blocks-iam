import type { ConsoleMessage, Page, Route } from "@playwright/test";
import { test, expect } from "../../support/test-base";

/**
 * blocks-iam#682: the activation and reset-password pages must survive fast password input
 * (password-manager fill, paste streams) without React error #185, and a render error on any
 * OIDC route must show the recovery screen instead of a blank page.
 *
 * Spec §10 steps 1-5. The activation link is simulated at the network edge, as in
 * oidc-activation.spec.ts: the runner cannot read the activation e-mail, and the defect and
 * its fix live in the deployed client, not in the API. The client under test is the real
 * dev build.
 */

const TENANT = "render-loop-tenant";
const ACTIVATE_QUERY = "code=render-loop-code&clientId=fixture-client&redirect_uri=https%3A%2F%2Fexample.com%2Fcb";
const ACTIVATE_PATH = `/oidc/activate/${TENANT}?${ACTIVATE_QUERY}`;
const RECOVER_PATH = `/oidc/recover/${TENANT}?code=render-loop-code`;
const VALID_PASSWORD = "Abcdef1!";
const PANEL = ".oidc-sci-fi-panel-border";

// React's production build reports #185 as "Minified React error #185"; the dev build says
// "Maximum update depth exceeded". Either one, or any other uncaught React error, fails the test.
const REACT_ERROR = /Minified React error|Maximum update depth|#185/i;

type Config = { template?: unknown };

const mockOidcUiConfig = async (page: Page, { template = {} }: Config = {}) => {
  await page.route("**/api/idp/oidc-ui-config*", (route: Route) =>
    route.fulfill({
      json: { captcha: null, collectPasswordOnActivation: true, template },
    }),
  );
};

const mockValidActivation = async (page: Page) => {
  await page.route("**/api/auth/validate-activation", (route: Route) =>
    route.fulfill({ json: { status: "Valid", firstName: "Jane", lastName: "Doe" } }),
  );
};

/** Collects uncaught page errors and console errors from React for the whole test. */
const watchReactErrors = (page: Page) => {
  const errors: string[] = [];
  page.on("pageerror", (error) => errors.push(`pageerror: ${error.message}`));
  page.on("console", (message: ConsoleMessage) => {
    if (message.type() === "error" && REACT_ERROR.test(message.text())) {
      errors.push(`console: ${message.text()}`);
    }
  });
  return errors;
};

/**
 * Sets both password fields from one `evaluate`, the way a password manager does: the native
 * value setter plus a bubbling `input` event, with no yield to React in between. `rounds`
 * repeats the pair in the same task, toggling between a weak and the final value, which is
 * the burst the #682 reproduction needed to pass React's nested-update limit.
 */
const fillBothInOneTick = (page: Page, value: string, rounds = 1) =>
  page.evaluate(
    ({ value, rounds }) => {
      const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")!.set!;
      const fields = ["password", "confirmPassword"].map(
        (name) => document.querySelector<HTMLInputElement>(`input[name="${name}"]`)!,
      );
      for (let i = rounds - 1; i >= 0; i--) {
        const next = i === 0 ? value : i % 2 === 0 ? value : "a";
        for (const field of fields) {
          setter.call(field, next);
          field.dispatchEvent(new Event("input", { bubbles: true }));
        }
      }
    },
    { value, rounds },
  );

/** The panel checker shows every rule as met: no rule row carries the red "not met" icon. */
const expectAllRequirementsMet = async (page: Page) => {
  const checker = page.locator(PANEL).locator("div").filter({
    has: page.getByRole("heading", { name: "Password Strength", exact: true }),
  }).last();
  await expect(checker).toBeVisible();
  await expect(checker.locator("li")).not.toHaveCount(0);
  await expect(checker.locator("li svg.text-red-500")).toHaveCount(0);
};

const clearPasswords = async (page: Page) => {
  await page.locator('input[name="password"]').fill("");
  await page.locator('input[name="confirmPassword"]').fill("");
};

test.describe("OIDC password panel render loop and recovery screen (blocks-iam#682)", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    // Spec §10: the animation panel must be visible, which needs a desktop width (>= 768 px).
    await page.setViewportSize({ width: 1280, height: 900 });
  });

  test("activation: keyboard entry, one-tick fill and a fast burst stay error-free, then submit reaches the success page (H1, H2, H5)", async ({
    page,
  }) => {
    const reactErrors = watchReactErrors(page);
    await mockOidcUiConfig(page);
    await mockValidActivation(page);
    let activatePayload: { code?: string; password?: string } | null = null;
    await page.route("**/api/auth/activate", async (route: Route) => {
      activatePayload = route.request().postDataJSON();
      await route.fulfill({ json: { isSuccess: true, errors: {} } });
    });

    await page.goto(ACTIVATE_PATH, { waitUntil: "domcontentloaded" });
    await expect(page.locator(PANEL)).toHaveCount(1, { timeout: 30_000 });
    const submit = page.locator('form button[type="submit"]');

    await test.step("[Positive] step 1: typing a valid password in both fields by keyboard enables submit, no React error (H1, H5)", async () => {
      await page.locator('input[name="password"]').pressSequentially(VALID_PASSWORD);
      await page.locator('input[name="confirmPassword"]').pressSequentially(VALID_PASSWORD);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      expect(reactErrors).toEqual([]);
    });

    await test.step("[Positive] step 2: both fields set in one evaluate (password-manager fill), no React #185 (H2)", async () => {
      await clearPasswords(page);
      await expect(submit).toBeDisabled();
      await fillBothInOneTick(page, VALID_PASSWORD);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      expect(reactErrors).toEqual([]);
    });

    await test.step("[Positive] step 2: a burst of 120 two-field fills in one task, the #682 reproduction, renders without #185 (H2)", async () => {
      await fillBothInOneTick(page, VALID_PASSWORD, 120);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      // A #185 unmounts the tree; the form must still be on screen.
      await expect(page.locator('input[name="confirmPassword"]')).toHaveValue(VALID_PASSWORD);
      expect(reactErrors).toEqual([]);
    });

    await test.step("[Positive] step 3: submit sends the password and lands on the success page (H5)", async () => {
      await submit.click();
      await expect(page).toHaveURL(/\/oidc\/activate-success/, { timeout: 30_000 });
      await expect(page.getByText(/account activated/i)).toBeVisible({ timeout: 30_000 });
      expect(activatePayload?.code).toBe("render-loop-code");
      expect(activatePayload?.password).toBe(VALID_PASSWORD);
      expect(reactErrors).toEqual([]);
    });
  });

  test("reset-password: keyboard entry, one-tick fill and a fast burst stay error-free (H6)", async ({ page }) => {
    const reactErrors = watchReactErrors(page);
    await mockOidcUiConfig(page);

    await page.goto(RECOVER_PATH, { waitUntil: "domcontentloaded" });
    await expect(page.locator(PANEL)).toHaveCount(1, { timeout: 30_000 });
    await expect(page.locator('input[name="password"]')).toBeVisible({ timeout: 30_000 });
    const submit = page.locator('form button[type="submit"]');

    // With the panel visible, reset-password gates its submit button only on the server's answer
    // (the button is gated client-side only when the checker is inline), so these steps check the
    // panel checker and the absence of React errors rather than a disabled button.
    await test.step("[Positive] step 4 (1): keyboard entry in both fields meets every rule, no React error", async () => {
      await page.locator('input[name="password"]').pressSequentially(VALID_PASSWORD);
      await page.locator('input[name="confirmPassword"]').pressSequentially(VALID_PASSWORD);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      expect(reactErrors).toEqual([]);
    });

    await test.step("[Positive] step 4 (2): one-tick fill and a 120-fill burst render without #185", async () => {
      await clearPasswords(page);
      await fillBothInOneTick(page, VALID_PASSWORD);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      await fillBothInOneTick(page, VALID_PASSWORD, 120);
      await expectAllRequirementsMet(page);
      await expect(submit).toBeEnabled();
      await expect(page.locator('input[name="confirmPassword"]')).toHaveValue(VALID_PASSWORD);
      expect(reactErrors).toEqual([]);
    });
  });

  test("a render error in ActivationForm shows the recovery screen, reports once without query values, and Try again reloads the same URL (C1, C2, C3)", async ({
    page,
  }) => {
    // The app has no test-only throw flag. A template label that is an object instead of a
    // string makes ActivationForm throw while rendering ("Objects are not valid as a React
    // child"); the recovery screen does not read that label, so it can still render.
    await mockOidcUiConfig(page, {
      template: { pages: { activation: { passwordLabel: { broken: true } } } },
    });
    await mockValidActivation(page);

    // Rollbar items are captured and answered here, so nothing reaches the real project.
    const rollbarItems: string[] = [];
    await page.route(/rollbar\.com\/api\/1\/item/, async (route: Route) => {
      rollbarItems.push(route.request().postData() ?? "");
      await route.fulfill({ json: { err: 0, result: { uuid: "e2e" } } });
    });

    await test.step("[Negative] the route error element renders the recovery screen instead of a blank page (C1)", async () => {
      await page.goto(ACTIVATE_PATH, { waitUntil: "domcontentloaded" });
      await expect(page.getByRole("heading", { name: /something went wrong/i })).toBeVisible({
        timeout: 30_000,
      });
      await expect(page.getByRole("button", { name: /^try again$/i })).toBeVisible();
      await expect(page.getByRole("link", { name: /back to login/i })).toBeVisible();
      await expect(page.locator('input[name="password"]')).toHaveCount(0);
    });

    await test.step("[Negative] one Rollbar item for the route error, carrying the route path and no query values (C2)", async () => {
      const routeItems = () =>
        rollbarItems.filter((body) => body.includes("oidc-route-error"));
      // Rollbar reports only when the host's runtime config carries a client token. A host that
      // serves the unreplaced placeholder (or nothing) sends no item at all; then the step can only
      // check that nothing carrying the code left the page, and says so in the report.
      const rollbarConfigured = await page.evaluate(() => {
        const env = (window as unknown as { __BLOCKS_ENV__?: Record<string, unknown> }).__BLOCKS_ENV__;
        const token = String(env?.BLOCKS_ROLLBAR_CLIENT_TOKEN ?? "");
        return token.length > 0 && !/^__.*__$/.test(token);
      });
      if (!rollbarConfigured) {
        test.info().annotations.push({
          type: "C2 not observable",
          description: "this host has no Rollbar client token in its runtime config, so the client sends no item",
        });
        await page.waitForLoadState("networkidle").catch(() => {});
        expect(rollbarItems.filter((body) => body.includes("render-loop-code"))).toEqual([]);
        return;
      }
      await expect.poll(() => routeItems().length, { timeout: 15_000 }).toBe(1);
      // Give a duplicate report time to arrive before asserting there is only one.
      await page.waitForLoadState("networkidle").catch(() => {});
      expect(routeItems()).toHaveLength(1);
      const item = routeItems()[0];
      expect(item).toContain(`/oidc/activate/${TENANT}`);
      expect(item).not.toContain("render-loop-code");
      expect(item).not.toContain("example.com");
      expect(item).not.toContain("fixture-client");
    });

    await test.step("[Positive] Try again reloads the same route with the original query (C3)", async () => {
      const before = page.url();
      const reload = page.waitForEvent("framenavigated", (frame) => frame === page.mainFrame());
      await page.getByRole("button", { name: /^try again$/i }).click();
      await reload;
      expect(page.url()).toBe(before);
      expect(new URL(page.url()).searchParams.get("code")).toBe("render-loop-code");
      expect(new URL(page.url()).searchParams.get("redirect_uri")).toBe("https://example.com/cb");
      // The broken template is still served, so the reload ends on the recovery screen again.
      await expect(page.getByRole("heading", { name: /something went wrong/i })).toBeVisible({
        timeout: 30_000,
      });
    });
  });
});
