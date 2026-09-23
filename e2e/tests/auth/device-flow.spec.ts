import { test, expect } from "../../support/test-base";

/**
 * RFC 8628 device authorization flow:
 *   - /device/:tenantId            (entry + code entry + consent)
 *   - /device/success?outcome=...  (terminal screen)
 *
 * Entry page renders one of:
 *   - "Missing Tenant" error card (when no tenantId)
 *   - code-entry form (idle)
 *   - login_required panel (auto-submit redirect)
 *   - "expired" terminal (auto-submit or decision-time)
 *   - "tenant_mismatch" terminal
 *   - consent panel with Allow / Deny
 *   - "error" terminal
 *
 * Backed by:
 *   POST /api/device/verify
 *   POST /api/device/decision
 *
 * All three are mocked per-step.
 */

const TENANT_ID = "tenant-xyz";
const ENTRY_URL = `/device/${TENANT_ID}`;

const mockOidcUiConfig = async (page: any) => {
  await page.route("**/api/idp/oidc-ui-config*", async (route: any) => {
    await route.fulfill({
      status: 200,
      contentType: "application/json",
      body: JSON.stringify({ captcha: null, template: {} }),
    });
  });
};

test.describe("Device flow — entry page", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("Device flow entry — missing tenant, idle form, code formatting, validation, ready/Allow/Deny, login_required, expired, tenant_mismatch", async ({
    page,
  }) => {
    await test.step("[Negative] Missing tenant renders the 'Missing Tenant' card", async () => {
      await page.goto("/device/%20", { waitUntil: "domcontentloaded" });

      await expect(page.getByText(/missing tenant/i)).toBeVisible({ timeout: 30_000 });
      await expect(
        page.getByText(/open the link exactly as displayed on your device/i),
      ).toBeVisible();
    });

    await test.step("[Positive] Idle entry shows the code input + Continue button", async () => {
      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await expect(page.locator("#device-user-code")).toBeVisible({ timeout: 30_000 });
      await expect(page.getByRole("button", { name: /continue/i })).toBeVisible();
    });

    await test.step("[Positive] Code input formats to ABCD-EFGH as the user types", async () => {
      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      const input = page.locator("#device-user-code");
      await expect(input).toBeVisible({ timeout: 30_000 });

      await input.fill("ABCDEFGH");
      await expect(input).toHaveValue("ABCD-EFGH");
    });

    await test.step("[Negative] Submitting an empty value shows a validation error", async () => {
      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      // The Continue button is disabled while the value is empty.
      await expect(page.getByRole("button", { name: /continue/i })).toBeDisabled({
        timeout: 30_000,
      });
    });

    await test.step("[Positive] Valid code + ready response swaps the form for the consent panel", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "ready",
            payload: {
              clientId: "test-client",
              clientName: "Test CLI",
              userCode: "ABCD-EFGH",
              approvalToken: "approval-token-1",
              scopes: ["openid", "profile", "email"],
              tenant: TENANT_ID,
              deviceName: "MacBook Pro",
            },
          }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      await expect(page.getByText("Authorize this device?")).toBeVisible({
        timeout: 15_000,
      });
      await expect(page.getByText("Test CLI")).toBeVisible();
      await expect(page.getByText("ABCD-EFGH")).toBeVisible();
      await expect(page.getByText("MacBook Pro")).toBeVisible();

      // Allow and Deny both visible.
      await expect(page.getByRole("button", { name: /^allow$/i })).toBeVisible();
      await expect(page.getByRole("button", { name: /^deny$/i })).toBeVisible();

      await page.unroute("**/api/device/verify").catch(() => {});
    });

    await test.step("[Positive] 'Allow' submits the decision and follows the redirect", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "ready",
            payload: {
              clientId: "test-client",
              userCode: "ABCD-EFGH",
              approvalToken: "approval-token-1",
              scopes: ["openid"],
              tenant: TENANT_ID,
            },
          }),
        });
      });

      let decisionPayload: any = null;
      await page.route("**/api/device/decision", async (route: any) => {
        decisionPayload = JSON.parse(route.request().postData() || "{}");
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            redirect: `/device/${TENANT_ID}/success?outcome=approved`,
          }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      await page.getByRole("button", { name: /^allow$/i }).click();

      // The decision endpoint receives the right payload.
      await expect.poll(() => decisionPayload, { timeout: 10_000 }).not.toBeNull();
      expect(decisionPayload.decision).toBe("allow");
      expect(decisionPayload.user_code).toBe("ABCD-EFGH");
      expect(decisionPayload.approvalToken).toBe("approval-token-1");

      // The page navigates to the success URL.
      await expect(page).toHaveURL(/\/device\/[^/]+\/success/, { timeout: 15_000 });
      await expect(page).toHaveURL(/outcome=approved/);

      await page.unroute("**/api/device/verify").catch(() => {});
      await page.unroute("**/api/device/decision").catch(() => {});
    });

    await test.step("[Positive] 'Deny' submits decision=deny", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "ready",
            payload: {
              clientId: "test-client",
              userCode: "ABCD-EFGH",
              approvalToken: "approval-token-2",
              scopes: ["openid"],
              tenant: TENANT_ID,
            },
          }),
        });
      });

      let decision: any = null;
      await page.route("**/api/device/decision", async (route: any) => {
        decision = JSON.parse(route.request().postData() || "{}");
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            redirect: `/device/${TENANT_ID}/success?outcome=denied`,
          }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      await page.getByRole("button", { name: /^deny$/i }).click();

      await expect.poll(() => decision, { timeout: 10_000 }).not.toBeNull();
      expect(decision.decision).toBe("deny");

      await expect(page).toHaveURL(/outcome=denied/);

      await page.unroute("**/api/device/verify").catch(() => {});
      await page.unroute("**/api/device/decision").catch(() => {});
    });

    await test.step("[Negative] login_required response redirects the user to /oidc/login", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "login_required",
            returnUrl: "/oidc/login?returnUrl=/device/tenant-xyz",
          }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      // The page navigates using window.location.assign → full reload, not
      // react-router. Wait for the URL to settle.
      await page.waitForURL(/\/oidc\/login/, { timeout: 15_000 });

      await page.unroute("**/api/device/verify").catch(() => {});
    });

    await test.step("[Negative] expired_token via manual submit shows the 'expired' terminal", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 500,
          contentType: "application/json",
          body: JSON.stringify({ errors: { error: "expired_token" } }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      await expect(page.getByText(/device code has expired/i)).toBeVisible({
        timeout: 15_000,
      });
      await expect(page.getByRole("link", { name: /start over/i })).toBeVisible();

      await page.unroute("**/api/device/verify").catch(() => {});
    });

    await test.step("[Negative] Auto-submitted expired_token clears the user_code and shows an inline error", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 500,
          contentType: "application/json",
          body: JSON.stringify({ errors: { error: "expired_token" } }),
        });
      });

      // Land with user_code in the URL. The auto-submit fires once on mount.
      await page.goto(`${ENTRY_URL}?user_code=ABCD-EFGH`, {
        waitUntil: "domcontentloaded",
      });

      // After the auto-submit fails, the URL drops user_code (so a refresh
      // doesn't re-trigger) and an inline error appears.
      await expect(page).not.toHaveURL(/user_code=/, { timeout: 15_000 });
      await expect(page.locator("#device-user-code")).toBeVisible();
      await expect(
        page.getByText(/that code has expired\. enter the fresh code shown on your device/i),
      ).toBeVisible();

      await page.unroute("**/api/device/verify").catch(() => {});
    });

    await test.step("[Negative] tenant_mismatch response shows the 'Wrong tenant' card", async () => {
      await page.route("**/api/device/verify", async (route: any) => {
        await route.fulfill({
          status: 200,
          contentType: "application/json",
          body: JSON.stringify({
            status: "ready",
            payload: {
              clientId: "test-client",
              userCode: "ABCD-EFGH",
              approvalToken: "approval-token-3",
              scopes: ["openid"],
              tenant: "tenant-other",
            },
          }),
        });
      });

      await page.goto(ENTRY_URL, { waitUntil: "domcontentloaded" });

      await page.locator("#device-user-code").fill("ABCDEFGH");
      await page.getByRole("button", { name: /continue/i }).click();

      await expect(page.getByText(/wrong tenant/i)).toBeVisible({ timeout: 15_000 });
      // The card names both tenants.
      await expect(page.getByText("tenant-other").first()).toBeVisible();
      await expect(page.getByText(TENANT_ID).first()).toBeVisible();
      await expect(page.getByRole("link", { name: /use another code/i })).toBeVisible();

      await page.unroute("**/api/device/verify").catch(() => {});
    });
  });
});

test.describe("Device flow — success page", () => {
  test.beforeEach(async ({ context, page }) => {
    await context.clearCookies();
    await mockOidcUiConfig(page);
  });

  test("Device flow success — approved, denied, expired, and neutral outcomes", async ({
    page,
  }) => {
    const SUCCESS_URL = `/device/${TENANT_ID}/success`;

    await test.step("[Positive] outcome=approved shows the success copy", async () => {
      await page.goto(`${SUCCESS_URL}?outcome=approved`, {
        waitUntil: "domcontentloaded",
      });

      await expect(page.getByText(/your device has been authorized/i)).toBeVisible({
        timeout: 30_000,
      });
      // After the succeed animation cascade completes, the shell's
      // SuccessState replaces the body — its subtitle is "Your device has
      // been authorized. You can close this window." (no "safely").
      await expect(page.getByText(/you can close this window/i)).toBeVisible();
    });

    await test.step("[Negative] outcome=denied shows the 'Authorization Declined' card", async () => {
      await page.goto(`${SUCCESS_URL}?outcome=denied`, {
        waitUntil: "domcontentloaded",
      });

      // For denied/expired the shell's heading flips to the failure copy
      // ("Authorization Declined") and the body keeps the X-circle +
      // "You can safely close this window" message; the SuccessState
      // subtitle ("The device was not authorized…") is only rendered for
      // phase === "succeeded", so we assert on what's actually visible.
      await expect(
        page.getByRole("heading", { name: /authorization declined/i }),
      ).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/you can safely close this window/i)).toBeVisible();
    });

    await test.step("[Negative] outcome=expired shows the 'Session Expired' card", async () => {
      await page.goto(`${SUCCESS_URL}?outcome=expired`, {
        waitUntil: "domcontentloaded",
      });

      await expect(
        page.getByRole("heading", { name: /session expired/i }),
      ).toBeVisible({ timeout: 30_000 });
      await expect(page.getByText(/you can safely close this window/i)).toBeVisible();
    });

    await test.step("[Negative] outcome=neutral falls back to generic copy", async () => {
      await page.goto(SUCCESS_URL, { waitUntil: "domcontentloaded" });

      await expect(page.getByText(/device flow finished/i)).toBeVisible({
        timeout: 30_000,
      });
    });
  });
});
