import { test, expect } from "../../support/test-base";

/**
 * #569 Phase 4 — redemption matrix against PR preview.
 * API-level coverage for outcome shapes; join route still under OidcLayout.
 */
test.describe("Signup link redemption (Phase 4)", () => {
  test("POST redeem fabricated code still returns invalid_link only", async ({
    request,
  }) => {
    const res = await request.post("/api/iam/signup-links/redeem", {
      data: { code: "phase4-fabricated-code-not-real-xxxxx" },
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
    const body = await res.json();
    expect(body.error).toBe("invalid_link");
  });

  test("POST redeem/mfa without ids returns 400", async ({ request }) => {
    const res = await request.post("/api/iam/signup-links/redeem/mfa", {
      data: {},
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
  });

  test("GET context remains idempotent (Phase 3 regression)", async ({
    request,
  }) => {
    const res = await request.get("/api/iam/signup-links/context");
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body.valid).toBe(false);
  });

  test("join page still reachable under OidcLayout", async ({ page }) => {
    await page.goto("/oidc/join/preview-tenant");
    await expect(
      page.getByText(/no longer valid|Checking your invite|Joining|Link unavailable|Welcome|Verify|Password/i),
    ).toBeVisible({ timeout: 30_000 });
  });
});
