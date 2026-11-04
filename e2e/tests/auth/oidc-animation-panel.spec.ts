import type { Page } from "@playwright/test";
import { test, expect } from "../../support/test-base";

const loginPath = "/oidc/login?tenant_id=panel-tenant&client_id=fixture-client&redirect_uri=https%3A%2F%2Fexample.com%2Fcb&response_type=code&scope=openid";
const panel = ".oidc-sci-fi-panel-border";
const card = ".oidc-scifi-root main > div";

async function mockConfig(page: Page, showAnimationPanel?: boolean) {
  await page.route("**/api/**", async route => {
    const path = new URL(route.request().url()).pathname;
    let data: unknown = {};
    if (path.endsWith("oidc-ui-config")) {
      data = { captcha: null, collectPasswordOnActivation: true, template: {
        ...(showAnimationPanel === undefined ? {} : { layout: { showAnimationPanel } }),
      } };
    } else if (path.endsWith("validate-activation")) {
      data = { status: "Valid", firstName: "Jane", lastName: "Doe" };
    } else if (path.endsWith("reset-password") || path.endsWith("activate")) {
      data = { isSuccess: false, errors: { password: "Password recently used" } };
    }
    await route.fulfill({ json: data });
  });
}

test.describe("OIDC animation panel", () => {
  test.beforeEach(async ({ context }) => { await context.clearCookies(); });

  for (const visible of [true, false]) {
    for (const theme of ["light", "dark"] as const) {
      for (const viewport of [{ width: 1440, height: 900 }, { width: 375, height: 812 }]) {
        test(`${visible ? "visible" : "hidden"}, ${theme}, ${viewport.width}: real shell pages and screenshots`, async ({ page }, testInfo) => {
          await page.setViewportSize(viewport);
          await page.emulateMedia({ colorScheme: theme, reducedMotion: "reduce" });
          await mockConfig(page, visible);
          for (const [name, path] of [
            ["signin", loginPath],
            ["activation", "/oidc/activate/panel-tenant?code=fixture"],
            ["reset-password", "/oidc/recover/panel-tenant?code=fixture"],
          ]) {
            await page.goto(path);
            await expect(page.locator(card)).toBeVisible();
            await expect(page.locator(panel)).toHaveCount(visible ? 1 : 0);
            if (!visible) {
              const box = await page.locator(card).boundingBox();
              expect(box!.width).toBeLessThanOrEqual(512);
              expect(Math.abs(box!.x + box!.width / 2 - viewport.width / 2)).toBeLessThanOrEqual(1);
            }
            if (name !== "signin") {
              // The visible-mode checker stays mounted inside the hidden panel on mobile.
              const checker = page.locator("h2").filter({ hasText: /^Password Strength$/ });
              await expect(checker).toHaveCount(1);
              if (!visible) {
                await expect(page.locator("form").getByRole("heading", { name: "Password Strength", exact: true })).toHaveCount(1);
              }
            }
            await testInfo.attach(`${name}.png`, { body: await page.screenshot(), contentType: "image/png" });
          }
        });
      }
    }
  }

  test("legacy config shows the panel; hiding adds no config request", async ({ page }) => {
    const counts: number[] = [];
    for (const visible of [undefined, false, true]) {
      await page.unrouteAll();
      await mockConfig(page, visible);
      let configRequests = 0;
      const count = (request: { url(): string }) => {
        if (request.url().includes("/api/idp/oidc-ui-config")) configRequests++;
      };
      page.on("request", count);
      await page.goto(loginPath);
      await expect(page.locator(panel)).toHaveCount(visible === false ? 0 : 1);
      await expect(page.locator("#oidc-email")).toBeVisible();
      await expect.poll(() => configRequests).toBe(1);
      page.off("request", count);
      counts.push(configRequests);
    }
    expect(counts).toEqual([1, 1, 1]);
  });

  for (const flow of ["activation", "reset-password"]) {
    test(`hidden ${flow}: inline checklist, submit gating, inline server errors`, async ({ page }) => {
      await mockConfig(page, false);
      await page.goto(flow === "activation" ? "/oidc/activate/panel-tenant?code=fixture" : "/oidc/recover/panel-tenant?code=fixture");
      const form = page.locator("form");
      const submit = form.locator('button[type="submit"]');
      const password = form.locator('input[name="password"]');
      const confirm = form.locator('input[name="confirmPassword"]');
      const checker = form.getByRole("heading", { name: "Password Strength", exact: true });
      await expect(checker).toHaveCount(1);
      await expect(page.locator(panel)).toHaveCount(0);
      await password.fill("abc");
      await confirm.fill("abc");
      await expect(submit).toBeDisabled();
      await password.fill("Compliant123!");
      await confirm.fill("Compliant123!");
      await expect(submit).toBeEnabled();
      await confirm.focus();
      await page.keyboard.press("Tab");
      if (flow === "reset-password") {
        await expect(page.getByRole("switch")).toBeFocused();
        await page.keyboard.press("Tab");
      }
      await expect(submit).toBeFocused();
      await submit.click();
      await expect(form.getByText("Password recently used", { exact: true })).toBeVisible();
    });
  }

  test("hidden device entry, invalid invitation and error page use the form-only shell", async ({ page }) => {
    await mockConfig(page, false);
    for (const path of ["/device/panel-tenant", "/oidc/invitation/panel-tenant#link=invalid", "/oidc/error?error=access_denied"]) {
      await page.goto(path);
      await expect(page.locator(card)).toBeVisible();
      await expect(page.locator(card)).toHaveClass(/max-w-lg/);
      await expect(page.locator(panel)).toHaveCount(0);
    }
  });

  test("hidden sign-in completes the form success path and redirects to the application", async ({ page }) => {
    await mockConfig(page, false);
    await page.route("**/api/oidc/login", route => route.fulfill({
      json: { success: true, redirect_uri: "https://example.com/panel-callback?code=fixture" },
    }));
    await page.route("https://example.com/panel-callback**", route => route.fulfill({
      contentType: "text/html", body: "<h1>Application callback</h1>",
    }));
    await page.goto(loginPath);
    await page.locator("#oidc-email").fill("user@example.test");
    await page.locator("#oidc-password").fill("Compliant123!");
    await page.locator('form button[type="submit"]').click();
    await expect(page).toHaveURL("https://example.com/panel-callback?code=fixture");
  });
});

// Optional live contract flow. Supply two fixture tenants, a T1 admin token and a
// T1 read-only token in addition to .env.e2e. Never run this against a production tenant.
test("live layout save/read, tenant isolation, invalid writes and restoration", async ({ request }) => {
  const tenant = process.env.E2E_OIDC_LAYOUT_TENANT;
  const otherTenant = process.env.E2E_OIDC_LAYOUT_OTHER_TENANT;
  const token = process.env.E2E_OIDC_LAYOUT_ADMIN_TOKEN;
  const readOnlyToken = process.env.E2E_OIDC_LAYOUT_READ_ONLY_TOKEN;
  test.skip(!tenant || !otherTenant || !token || !readOnlyToken, "Requires two fixture tenants and admin/read-only tokens");
  const headers = { "X-Blocks-Key": tenant!, Authorization: `Bearer ${token}` };
  const originalResponse = await request.get("/oidc-template", { headers });
  expect(originalResponse.ok()).toBeTruthy();
  const original = (await originalResponse.json()).template;
  test.skip(!original, "Fixture tenant must already have a template so cleanup can restore it exactly");
  const otherConfig = `/api/idp/oidc-ui-config?tenantId=${encodeURIComponent(otherTenant!)}`;
  const otherBefore = await (await request.get(otherConfig)).json();
  try {
    const save = await request.put("/oidc-template", { headers, data: { ...original, layout: { showAnimationPanel: false } } });
    expect(save.status()).toBe(200);
    expect((await save.json()).isSuccess).toBe(true);
    const saved = (await (await request.get("/oidc-template", { headers })).json()).template;
    expect(saved.layout.showAnimationPanel).toBe(false);
    const config = await request.get(`/api/idp/oidc-ui-config?tenantId=${encodeURIComponent(tenant!)}`);
    expect(config.headers()["cache-control"]).toContain("no-store");
    expect((await config.json()).template.layout.showAnimationPanel).toBe(false);
    expect(await (await request.get(otherConfig)).json()).toEqual(otherBefore);
    for (const value of ["false", 0, "yes"]) {
      expect((await request.put("/oidc-template", { headers, data: { ...original, layout: { showAnimationPanel: value } } })).status()).toBe(400);
      expect((await (await request.get("/oidc-template", { headers })).json()).template).toEqual(saved);
    }
    const forbidden = await request.put("/oidc-template", { headers: { ...headers, Authorization: `Bearer ${readOnlyToken}` }, data: original });
    expect([401, 403]).toContain(forbidden.status());
    const invalid = await request.put("/oidc-template", { headers, data: { ...original, branding: { ...original.branding, brandName: "" }, layout: { showAnimationPanel: true } } });
    expect(invalid.status()).toBe(400);
    expect((await (await request.get("/oidc-template", { headers })).json()).template).toEqual(saved);
    const legacy = { ...original };
    delete legacy.layout;
    expect((await request.put("/oidc-template", { headers, data: legacy })).status()).toBe(200);
    expect((await (await request.get("/oidc-template", { headers })).json()).template.layout.showAnimationPanel).toBe(true);
  } finally {
    const restore = await request.put("/oidc-template", { headers, data: original });
    expect(restore.ok()).toBeTruthy();
  }
});
