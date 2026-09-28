import { test, expect } from "../../support/test-base";

/**
 * #568 Phase 3 — Signup link redemption against PR preview.
 * Anonymous context is idempotent; redeem refuses unknown codes with a single
 * invalid_link shape (C1/C2). Join route renders under OidcLayout.
 */
test.describe("Signup link redemption (Phase 3)", () => {
  test("GET context without code returns valid:false", async ({ request }) => {
    const res = await request.get("/api/iam/signup-links/context");
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    expect(body).toEqual({ valid: false });
  });

  test("POST redeem with fabricated code returns invalid_link", async ({
    request,
  }) => {
    const res = await request.post("/api/iam/signup-links/redeem", {
      data: { code: "this-is-not-a-real-signup-link-code-xxxxx" },
    });
    expect(res.status()).toBeGreaterThanOrEqual(400);
    const body = await res.json();
    expect(body.error).toBe("invalid_link");
  });

  test("join page route is reachable under OidcLayout", async ({ page }) => {
    await page.goto("/oidc/join/preview-tenant");
    await expect(
      page.getByText(/no longer valid|Checking your invite|Joining|Link unavailable/i),
    ).toBeVisible({ timeout: 30_000 });
  });
});
