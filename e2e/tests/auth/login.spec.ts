import { test, expect } from "../../support/test-base";
import { e2eCredentials } from "../../support/env";
import { loginThroughOidc } from "../../support/login-helper";
import { iamApi } from "../../support/iam-api";

test.describe("Authentication", () => {
  test.beforeAll(() => {
    e2eCredentials();
  });

  test("logs in through iam and lands on the profile page", async ({ page }) => {
    test.setTimeout(120_000);

    const holdMs = Number(process.env.E2E_HOLD_MS ?? 0);
    if (holdMs > 0) test.setTimeout(holdMs + 120_000);

    await loginThroughOidc(page);

    // Assert the profile actually rendered — not just that the route changed.
    await expect(page).toHaveURL(/\/app\/profile/);
    await expect(page.getByRole("heading", { name: "Account details" })).toBeVisible({
      timeout: 20_000,
    });

    // Persist the authenticated session for future specs to reuse.
    await page.context().storageState({ path: "fixtures/auth.json" });

    // blocks-iam#668 §10.1 step 2 (H5 negative, H12): this tenant runs with multi-org off, so the
    // token carries no organization status and `auth/me` must not report one.
    await test.step("[Positive] H5/H12: auth/me has no org_status with multi-org off", async () => {
      const me = await iamApi<Record<string, unknown>>(page, "GET", "/api/auth/me");
      expect(me.status).toBe(200);
      expect(me.json).not.toHaveProperty("org_status");
      expect(me.raw).not.toContain("org_status");
    });

    // Optionally keep the browser open to inspect the result before it closes.
    // e.g. E2E_HOLD_MS=120000 npm run test:headed
    if (holdMs > 0) {
      await page.waitForTimeout(holdMs);
    }
  });
});
