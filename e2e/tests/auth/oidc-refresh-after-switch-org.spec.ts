import type { Page, Request } from "@playwright/test";
import { test, expect } from "../../support/test-base";
import { loginThroughOidc } from "../../support/login-helper";
import {
  iamApi,
  oidcRefresh,
  readAccessTokenClaim,
  resolveTenantId,
  type TokenCallOutcome,
} from "../../support/iam-api";

/**
 * blocks-iam#624: a hosted-OIDC session keeps refreshing through /oidc/token after
 * POST /auth/switch-org and POST /auth/refresh.
 *
 * Before the fix both endpoints rotated the refresh token without its OIDC client, so the
 * next /oidc/token refresh failed with 401 invalid_client and the app signed the user out.
 *
 * Covers spec §10 steps 1-4, 6 and 7:
 *   H9      /oidc/token refresh on a fresh hosted lineage (x3)
 *   H6      switch-org returns 200 and the new access token carries the target org_id
 *   H3/H12  /oidc/token refresh after switch-org returns 200
 *   H4/H5   /auth/refresh returns 200 and the next /oidc/token refresh returns 200
 *   C6      switch-org to a non-member organization returns 400 organization_not_available
 *           and leaves both cookies untouched
 *
 * Every call runs in-page with credentials included, so rotated cookies land in the browser.
 * A refresh made outside the browser would rotate the token while the browser kept the old
 * cookie, and a later replay of that cookie is treated as theft.
 *
 * Shared account: the test needs an account that belongs to at least two organizations. It
 * switches away from the current organization and always switches back in `finally`. It never
 * logs token or cookie values: assertions compare them as booleans.
 */

const ORG_CLAIM = "org_id";

type MyOrganizations = {
  isSuccess?: boolean;
  errors?: Record<string, string> | null;
  organizations?: { itemId: string; name?: string }[];
};

/** Status and error fields only; the switch-org body carries tokens and is never surfaced. */
async function switchOrganization(
  page: Page,
  organizationId: string,
  tenant: string,
): Promise<TokenCallOutcome> {
  const res = await iamApi<{ error?: string; error_description?: string }>(
    page,
    "POST",
    "/api/auth/switch-org",
    { organization_id: organizationId },
    tenant,
  );
  return {
    status: res.status,
    error: res.json?.error,
    errorDescription: res.json?.error_description,
  };
}

async function authRefresh(page: Page, tenant: string): Promise<TokenCallOutcome> {
  const res = await iamApi<{ error?: string; error_description?: string }>(
    page,
    "POST",
    "/api/auth/refresh",
    {},
    tenant,
  );
  return {
    status: res.status,
    error: res.json?.error,
    errorDescription: res.json?.error_description,
  };
}

function summarize(outcome: TokenCallOutcome): string {
  return `HTTP ${outcome.status}${outcome.error ? ` ${outcome.error}` : ""}${
    outcome.errorDescription ? ` (${outcome.errorDescription})` : ""
  }`;
}

/** Name -> value of the session cookies, for an equality check that never prints them. */
async function sessionCookies(page: Page): Promise<Map<string, string>> {
  const host = new URL(page.url()).hostname;
  const cookies = await page.context().cookies();
  return new Map(
    cookies
      .filter((c) => c.name === host || c.name.startsWith("rt_"))
      .map((c) => [c.name, c.value]),
  );
}

function sameCookies(a: Map<string, string>, b: Map<string, string>): boolean {
  return a.size === b.size && [...a].every(([name, value]) => b.get(name) === value);
}

async function currentOrganization(page: Page): Promise<string | undefined> {
  const claim = await readAccessTokenClaim(page, ORG_CLAIM);
  return typeof claim === "string" && claim ? claim : undefined;
}

/** The client the hosted login ran for: the lineage's client, which /oidc/token must name. */
async function runtimeClientId(page: Page): Promise<string | undefined> {
  return page.evaluate(() => {
    const env = (window as unknown as { __BLOCKS_ENV__?: Record<string, string | undefined> })
      .__BLOCKS_ENV__;
    const usable = (v?: string) => (v && !v.startsWith("__BLOCKS_") ? v : undefined);
    return usable(env?.BLOCKS_IAM_CLIENT_ID) ?? usable(env?.BLOCKS_OIDC_CLIENT_ID);
  });
}

test.describe("OIDC refresh after switch-org and /auth/refresh (blocks-iam#624)", () => {
  // Start with no saved session at all, so the login below begins a new lineage whose client is
  // known. Clearing only the cookies kept the saved auth-storage in localStorage.
  test.use({ storageState: { cookies: [], origins: [] } });

  test("a hosted-OIDC session keeps refreshing through /oidc/token", async ({ page }) => {
    // ---- Step 1: fresh hosted OIDC login, recording the client it ran for ----
    const loginClientIds: string[] = [];
    const recordClientId = (request: Request) => {
      const url = new URL(request.url());
      // Only the OIDC / IdP legs of the login name the application's client.
      if (!/\/(oidc|idp)\//.test(url.pathname)) return;
      const clientId = url.searchParams.get("client_id");
      if (clientId) loginClientIds.push(clientId);
    };

    await test.step("Sign in through the hosted OIDC login", async () => {
      // Start at /login, not "/": "/" always routes through /app/profile before the auth guard
      // sends a signed-out user to /login, and loginFresh takes that transient URL for a session.
      page.on("request", recordClientId);
      try {
        await loginThroughOidc(page);
      } finally {
        page.off("request", recordClientId);
      }
    });

    const clientId = loginClientIds[0] ?? (await runtimeClientId(page));
    expect(
      clientId,
      "Could not determine the OIDC client id of the hosted login (no client_id on any login request and no BLOCKS_IAM_CLIENT_ID / BLOCKS_OIDC_CLIENT_ID in the runtime env).",
    ).toBeTruthy();

    const tenant = await resolveTenantId(page);
    const originalOrganization = await currentOrganization(page);
    expect(
      originalOrganization,
      `The access token after login carries no ${ORG_CLAIM} claim, so the organization to restore is unknown.`,
    ).toBeTruthy();

    // ---- Precondition: at least two organizations ----
    const mine = await iamApi<MyOrganizations>(
      page,
      "GET",
      "/api/iam/organizations/my",
      undefined,
      tenant,
    );
    expect(mine.status, `GET /api/iam/organizations/my returned HTTP ${mine.status}`).toBe(200);
    const memberships = (mine.json.organizations ?? []).map((o) => o.itemId).filter(Boolean);
    const targetOrganization = memberships.find((id) => id !== originalOrganization);
    if (!targetOrganization) {
      const reason = mine.json.errors ? ` Server said: ${JSON.stringify(mine.json.errors)}.` : "";
      throw new Error(
        `The E2E account (E2E_USERNAME) must belong to at least two organizations in this tenant ` +
          `so switch-org has somewhere to go; it belongs to ${memberships.length} ` +
          `(current: ${originalOrganization}).${reason} Add the account to a second organization ` +
          `and re-run.`,
      );
    }

    try {
      await test.step("[H9] /oidc/token refreshes a fresh hosted lineage, three times", async () => {
        for (let i = 1; i <= 3; i++) {
          const outcome = await oidcRefresh(page, clientId!, tenant);
          expect(
            outcome.status,
            `/oidc/token refresh #${i} before switching: ${summarize(outcome)}`,
          ).toBe(200);
        }
      });

      await test.step("[H6] switch-org to another organization returns 200 with the new org_id", async () => {
        const outcome = await switchOrganization(page, targetOrganization, tenant);
        expect(outcome.status, `switch-org to ${targetOrganization}: ${summarize(outcome)}`).toBe(
          200,
        );
        expect(await currentOrganization(page)).toBe(targetOrganization);
      });

      await test.step("[H3/H12] /oidc/token refresh after switch-org returns 200, not invalid_client", async () => {
        const outcome = await oidcRefresh(page, clientId!, tenant);
        expect(outcome.status, `/oidc/token refresh after switch-org: ${summarize(outcome)}`).toBe(
          200,
        );
        expect(await currentOrganization(page), "the refresh keeps the switched organization").toBe(
          targetOrganization,
        );
      });

      await test.step("[H4/H5] /auth/refresh returns 200 and /oidc/token still refreshes after it", async () => {
        const viaAuthRefresh = await authRefresh(page, tenant);
        expect(viaAuthRefresh.status, `/auth/refresh: ${summarize(viaAuthRefresh)}`).toBe(200);

        const viaOidc = await oidcRefresh(page, clientId!, tenant);
        expect(
          viaOidc.status,
          `/oidc/token refresh after /auth/refresh: ${summarize(viaOidc)}`,
        ).toBe(200);
      });

      await test.step("[C6] switch-org to a non-member organization is refused and leaves both cookies alone", async () => {
        const before = await sessionCookies(page);
        expect(
          before.size,
          "expected the access and rt_ session cookies to be present",
        ).toBeGreaterThanOrEqual(2);

        const notAMember = `e2e-not-a-member-${Date.now()}`;
        const outcome = await switchOrganization(page, notAMember, tenant);
        expect(
          outcome.status,
          `switch-org to a non-member organization: ${summarize(outcome)}`,
        ).toBe(400);
        expect(outcome.error).toBe("organization_not_available");

        const after = await sessionCookies(page);
        expect(
          sameCookies(before, after),
          "both session cookies are unchanged after a refused switch",
        ).toBe(true);
      });
    } finally {
      // Leave the shared account where it started, whatever happened above.
      if ((await currentOrganization(page)) !== originalOrganization) {
        const restored = await switchOrganization(page, originalOrganization!, tenant);
        expect
          .soft(
            restored.status,
            `restoring organization ${originalOrganization}: ${summarize(restored)}`,
          )
          .toBe(200);
      }
    }
  });
});
