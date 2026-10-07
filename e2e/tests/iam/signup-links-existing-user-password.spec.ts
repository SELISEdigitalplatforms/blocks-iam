import { request as pwRequest, type APIRequestContext, type Page } from "@playwright/test";
import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";
import { e2eCredentials } from "../../support/env";

/**
 * #593 — existing active users confirm their password before a signup link signs them in.
 * Runs against the PR preview. The full flow uses the E2E user itself as the invitee:
 * it already exists and has a password, so redeem must stop at the password step.
 * One wrong attempt is followed by the correct one, which resets the failure counter.
 */

const PERM_RESOURCE = "blocks-iam::iam::manage-signup-links";
const IAM_CLIENT_ID = "a5831e15-e193-4a4f-8e10-d04a4ad1705b";
const BASE = (process.env.E2E_BASE_URL || "").replace(/\/$/, "");
const IAM_REDIRECT = `${BASE}/login/callback`;

type Mutation = { isSuccess?: boolean; itemId?: string; errors?: Record<string, string> | null };
type Config = { itemId: string; requireExistingUserPassword: boolean };
type Generate = { linkId: string; url: string; emailAlreadyExists: boolean };
type Perm = { itemId: string; resource: string };

async function ensureManageSignupLinksPermission(page: Page) {
  const listed = await iamApi<{ data: Perm[] }>(page, "POST", "/api/iam/permissions", { page: 0, pageSize: 300 });
  expect(listed.status).toBe(200);
  const perm = (listed.json.data || []).find((p) => p.resource === PERM_RESOURCE);
  expect(perm, "manage-signup-links permission must exist on the preview tenant").toBeTruthy();
  const assign = await iamApi(page, "POST", "/api/iam/roles/assign-permissions", {
    slug: "clouduser",
    addPermissions: [perm!.itemId],
    removePermissions: [],
  });
  expect(assign.status).toBe(200);
}

async function anonContext(tenant: string): Promise<APIRequestContext> {
  return pwRequest.newContext({
    baseURL: BASE,
    ignoreHTTPSErrors: true,
    extraHTTPHeaders: { "X-Blocks-Key": tenant, Accept: "application/json" },
  });
}

async function generateFor(page: Page, tenant: string, cfgId: string, email: string) {
  const body = { configurationId: cfgId, email, firstName: "E2E", lastName: "Existing" };
  let gen = await iamApi<Generate & { errors?: Record<string, string> }>(
    page, "POST", "/api/iam/signup-links", { ...body, organizationId: "default" }, tenant,
  );
  if (gen.status !== 200 && gen.json.errors?.OrganizationId) {
    gen = await iamApi(page, "POST", "/api/iam/signup-links", body, tenant);
  }
  expect(gen.status, JSON.stringify(gen.json)).toBe(200);
  const code = new URLSearchParams(new URL(gen.json.url).hash.slice(1)).get("link");
  expect(code).toBeTruthy();
  return { linkId: gen.json.linkId, url: gen.json.url, code: code!, emailAlreadyExists: gen.json.emailAlreadyExists };
}

async function createConfig(page: Page, tenant: string, label: string): Promise<string> {
  const created = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
    name: `E2E 593 ${label} ${Date.now()}`,
    defaultRoles: ["clouduser"],
    defaultPermissions: [],
    clientId: IAM_CLIENT_ID,
    redirectUri: IAM_REDIRECT,
    defaultForwardedTo: "/app/profile",
    credentialMode: "Passwordless",
    defaultLifetimeMinutes: 30,
  }, tenant);
  expect(created.status, JSON.stringify(created.json)).toBe(200);
  return created.json.itemId!;
}

async function cleanupConfig(page: Page, tenant: string, cfgId: string) {
  await iamApi(page, "POST", "/api/iam/signup-links/revoke-by-configuration", { configurationId: cfgId }, tenant);
  await iamApi(page, "POST", `/api/iam/signup-links/configurations/${cfgId}/archive`, {}, tenant);
}

/** The generated URL may name another host; the path and fragment are what the page needs. */
function onPreview(url: string): string {
  const parsed = new URL(url);
  return `${BASE}${parsed.pathname}${parsed.search}${parsed.hash}`;
}

test.describe("Signup link existing-user password step (#593)", () => {
  test("authenticate contract: missing fields and unknown redemption ids", async ({ page }) => {
    await ensureAuthenticated(page);
    const tenant = await resolveTenantId(page);
    const anon = await anonContext(tenant);
    try {
      const missing = await anon.post("/api/iam/signup-links/redeem/authenticate", { data: {} });
      expect(missing.status()).toBe(400);
      expect((await missing.json()).error).toBe("invalid_request");

      const unknown = await anon.post("/api/iam/signup-links/redeem/authenticate", {
        data: { redemptionId: "not-a-real-redemption-id-000000000000000", password: "whatever" },
      });
      expect(unknown.status()).toBe(400);
      expect((await unknown.json()).error).toBe("invalid_redemption");
    } finally {
      await anon.dispose();
    }
  });

  test("config default, PATCH, and the full password step for an existing user", async ({ page }) => {
    test.setTimeout(240_000);
    await ensureAuthenticated(page);
    await ensureManageSignupLinksPermission(page);
    const tenant = await resolveTenantId(page);
    const { email, password } = e2eCredentials();

    // H1: new configurations require the password step by default.
    const created = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      name: `E2E 593 ${Date.now()}`,
      defaultRoles: ["clouduser"],
      defaultPermissions: [],
      clientId: IAM_CLIENT_ID,
      redirectUri: IAM_REDIRECT,
      defaultForwardedTo: "/app/profile",
      credentialMode: "Passwordless",
      defaultLifetimeMinutes: 30,
    }, tenant);
    expect(created.status, JSON.stringify(created.json)).toBe(200);
    const cfgId = created.json.itemId!;

    try {
      const read = await iamApi<Config>(page, "GET", `/api/iam/signup-links/configurations/${cfgId}`, undefined, tenant);
      expect(read.status).toBe(200);
      expect(read.json.requireExistingUserPassword).toBe(true);

      // H4: PATCH turns it off and back on.
      const off = await iamApi<Mutation>(page, "PATCH", `/api/iam/signup-links/configurations/${cfgId}`,
        { requireExistingUserPassword: false }, tenant);
      expect(off.status, JSON.stringify(off.json)).toBe(200);
      const readOff = await iamApi<Config>(page, "GET", `/api/iam/signup-links/configurations/${cfgId}`, undefined, tenant);
      expect(readOff.json.requireExistingUserPassword).toBe(false);
      const on = await iamApi<Mutation>(page, "PATCH", `/api/iam/signup-links/configurations/${cfgId}`,
        { requireExistingUserPassword: true }, tenant);
      expect(on.status, JSON.stringify(on.json)).toBe(200);

      // H6: the invitee already has an account, so redeem stops at the password step.
      const link = await generateFor(page, tenant, cfgId, email);
      expect(link.emailAlreadyExists).toBe(true);

      const anon = await anonContext(tenant);
      try {
        const redeem = await anon.post("/api/iam/signup-links/redeem", { data: { code: link.code } });
        expect(redeem.status()).toBe(200);
        const step = await redeem.json();
        expect(step.error).toBe("authentication_required");
        expect(typeof step.redemptionId).toBe("string");
        expect(step.maskedEmail).not.toBe(email);
        expect(step.authorizeUrl ?? null).toBeNull();
        expect(step.loginUrl ?? null).toBeNull();

        // H8: a wrong password is refused and the step stays open.
        const wrong = await anon.post("/api/iam/signup-links/redeem/authenticate", {
          data: { redemptionId: step.redemptionId, password: `${password}-wrong` },
        });
        expect(wrong.status()).toBe(400);
        expect((await wrong.json()).error).toBe("invalid_username_password");

        // H7: the correct password finishes the redemption.
        const ok = await anon.post("/api/iam/signup-links/redeem/authenticate", {
          data: { redemptionId: step.redemptionId, password },
        });
        const okBody = await ok.json();
        expect(ok.status(), JSON.stringify({ error: okBody.error })).toBe(200);
        expect(okBody.error ?? null).toBeNull();
        expect(Boolean(okBody.authorizeUrl || okBody.loginUrl || okBody.mfaId)).toBe(true);

        // C5: the redemption id is single use.
        const replay = await anon.post("/api/iam/signup-links/redeem/authenticate", {
          data: { redemptionId: step.redemptionId, password },
        });
        expect(replay.status()).toBe(400);
        expect((await replay.json()).error).toBe("invalid_redemption");
      } finally {
        await anon.dispose();
      }
    } finally {
      await iamApi(page, "POST", "/api/iam/signup-links/revoke-by-configuration", { configurationId: cfgId }, tenant);
      await iamApi(page, "POST", `/api/iam/signup-links/configurations/${cfgId}/archive`, {}, tenant);
    }
  });

  test("hosted page: Confirm it's you, wrong password stays inline, correct password continues", async ({ page, browser }) => {
    test.setTimeout(240_000);
    await ensureAuthenticated(page);
    await ensureManageSignupLinksPermission(page);
    const tenant = await resolveTenantId(page);
    const { email, password } = e2eCredentials();
    const cfgId = await createConfig(page, tenant, "ui");

    // The invitee opens the link in a fresh browser with no session.
    const invitee = await browser.newContext({ ignoreHTTPSErrors: true, storageState: undefined });
    try {
      const link = await generateFor(page, tenant, cfgId, email);
      expect(link.emailAlreadyExists).toBe(true);
      const tab = await invitee.newPage();
      await tab.goto(onPreview(link.url));

      // H14: the password step, with the masked email and the password field focused.
      const masked = tab.getByTestId("masked-email");
      await expect(masked).toBeVisible({ timeout: 60_000 });
      await expect(masked).not.toHaveText(email);
      const field = tab.locator("#invitation-password");
      await expect(field).toBeFocused();

      // C15: a wrong password shows the inline error, clears and refocuses the field,
      // and does not send the invitee to /login.
      await field.fill(`${password}-wrong`);
      await tab.getByRole("button", { name: "Continue" }).click();
      await expect(tab.locator("#invitation-password-error")).toBeVisible({ timeout: 30_000 });
      await expect(field).toHaveValue("");
      await expect(field).toBeFocused();
      expect(new URL(tab.url()).pathname).not.toMatch(/^\/login/);
      await expect(masked).toBeVisible();

      // H15: the correct password continues through the existing navigation.
      await field.fill(password);
      await tab.getByRole("button", { name: "Continue" }).click();
      await expect(masked).toBeHidden({ timeout: 60_000 });
      await expect(tab.locator("#invitation-password-error")).toHaveCount(0);
    } finally {
      await invitee.close();
      await cleanupConfig(page, tenant, cfgId);
    }
  });
});
