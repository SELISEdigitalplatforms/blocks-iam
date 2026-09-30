import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * #566 Phase 1 — Signup link configuration CRUD against the PR preview API.
 * Covers ticket §7 happy path (H1–H5) and critical validation (C1–C4);
 * C5 (permission deny) is covered by temporarily removing the role grant.
 */

const PERM_RESOURCE = "blocks-iam::iam::manage-signup-links";
const IAM_CLIENT_ID = "a5831e15-e193-4a4f-8e10-d04a4ad1705b";
const IAM_REDIRECT = `${process.env.E2E_BASE_URL?.replace(/\/$/, "")}/login/callback`;

type Mutation = { isSuccess?: boolean; itemId?: string; errors?: Record<string, string> | null };
type Config = {
  itemId: string;
  name: string;
  description?: string;
  defaultRoles: string[];
  clientId: string;
  redirectUri: string;
  defaultForwardedTo?: string;
  credentialMode: string;
  defaultLifetimeMinutes: number;
  defaultMaxRedemptions: number | null;
  isActive: boolean;
  lastUpdatedDate?: string;
};
type List = { items: Config[]; totalCount: number };
type Perm = { itemId: string; resource: string; name: string };

async function ensureManageSignupLinksPermission(page: import("@playwright/test").Page) {
  const listed = await iamApi<{ totalCount: number; data: Perm[] }>(page, "POST", "/api/iam/permissions", {
    page: 0,
    pageSize: 300,
  });
  expect(listed.status).toBe(200);
  let perm = (listed.json.data || []).find((p) => p.resource === PERM_RESOURCE);
  if (!perm) {
    const created = await iamApi<Mutation>(page, "POST", "/api/iam/permissions/create", {
      name: "Manage Signup Links",
      type: 1,
      description: "Create, update, archive and query signup link configurations.",
      resource: PERM_RESOURCE,
      resourceGroup: "blocks-iam",
      tags: [],
      dependentPermissions: [],
      isBuiltIn: true,
      permissionSeverity: 3,
    });
    expect(created.status).toBe(200);
    expect(created.json.isSuccess).toBe(true);
    perm = { itemId: created.json.itemId!, resource: PERM_RESOURCE, name: "Manage Signup Links" };
  }
  const assign = await iamApi(page, "POST", "/api/iam/roles/assign-permissions", {
    slug: "clouduser",
    addPermissions: [perm.itemId],
    removePermissions: [],
  });
  expect(assign.status).toBe(200);
  return perm.itemId;
}

function uniqueName(prefix: string) {
  return `${prefix} ${Date.now()}-${Math.floor(Math.random() * 1e4)}`;
}

test.describe("Signup link configurations (#566)", () => {
  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
    await ensureManageSignupLinksPermission(page);
  });

  test("H1–H5 CRUD happy path + C1–C4 validation", async ({ page }) => {
    test.setTimeout(180_000);
    const createdIds: string[] = [];
    const tenant = await resolveTenantId(page);

    const baseBody = {
      defaultRoles: ["clouduser"],
      defaultPermissions: [],
      clientId: IAM_CLIENT_ID,
      redirectUri: IAM_REDIRECT,
      defaultForwardedTo: "/app/profile",
      credentialMode: "PasswordRequired",
      defaultLifetimeMinutes: 1440,
    };

    // H1 — create
    const name1 = uniqueName("E2E Construct Site Manager");
    const create1 = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...baseBody,
      name: name1,
      description: "phase1 e2e",
    }, tenant);
    expect(create1.status, JSON.stringify(create1.json)).toBe(200);
    expect(create1.json.isSuccess).toBe(true);
    expect(create1.json.itemId).toBeTruthy();
    createdIds.push(create1.json.itemId!);

    const get1 = await iamApi<Config>(
      page,
      "GET",
      `/api/iam/signup-links/configurations/${create1.json.itemId}`,
      undefined,
      tenant,
    );
    expect(get1.status).toBe(200);
    expect(get1.json.isActive).toBe(true);
    expect(get1.json.name).toBe(name1);

    // H2 — defaults when ttl / maxRedemptions omitted
    const name2 = uniqueName("E2E Defaults");
    const { defaultLifetimeMinutes: _omitTtl, ...withoutTtl } = baseBody;
    const create2 = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...withoutTtl,
      name: name2,
    }, tenant);
    expect(create2.status, JSON.stringify(create2.json)).toBe(200);
    expect(create2.json.isSuccess).toBe(true);
    createdIds.push(create2.json.itemId!);
    const get2 = await iamApi<Config>(
      page,
      "GET",
      `/api/iam/signup-links/configurations/${create2.json.itemId}`,
      undefined,
      tenant,
    );
    expect(get2.json.defaultLifetimeMinutes).toBe(1440);
    expect(get2.json.defaultMaxRedemptions).toBeNull();

    // H3 — patch only description
    const beforePatch = get1.json.lastUpdatedDate;
    const patch = await iamApi<Mutation>(
      page,
      "PATCH",
      `/api/iam/signup-links/configurations/${create1.json.itemId}`,
      { description: "patched-by-e2e" },
      tenant,
    );
    expect(patch.status, JSON.stringify(patch.json)).toBe(200);
    expect(patch.json.isSuccess).toBe(true);
    const getPatched = await iamApi<Config>(
      page,
      "GET",
      `/api/iam/signup-links/configurations/${create1.json.itemId}`,
      undefined,
      tenant,
    );
    expect(getPatched.json.description).toBe("patched-by-e2e");
    expect(getPatched.json.clientId).toBe(IAM_CLIENT_ID);
    expect(getPatched.json.redirectUri).toBe(IAM_REDIRECT);
    expect(getPatched.json.credentialMode).toBe("PasswordRequired");
    expect(getPatched.json.defaultRoles).toEqual(["clouduser"]);
    if (beforePatch && getPatched.json.lastUpdatedDate) {
      expect(getPatched.json.lastUpdatedDate >= beforePatch).toBe(true);
    }

    // H5 — query returns tenant configs
    const queryActive = await iamApi<List>(page, "POST", "/api/iam/signup-links/configurations/query", {
      page: 0,
      pageSize: 50,
      includeInactive: false,
      search: "E2E",
    }, tenant);
    expect(queryActive.status).toBe(200);
    expect(queryActive.json.totalCount).toBeGreaterThanOrEqual(2);
    expect(queryActive.json.items.some((i) => i.itemId === create1.json.itemId)).toBe(true);

    // H4 — archive then excluded from active query, still get-by-id
    const archive = await iamApi<Mutation>(
      page,
      "POST",
      `/api/iam/signup-links/configurations/${create1.json.itemId}/archive`,
      {},
      tenant,
    );
    expect(archive.status, JSON.stringify(archive.json)).toBe(200);
    expect(archive.json.isSuccess).toBe(true);

    const queryAfter = await iamApi<List>(page, "POST", "/api/iam/signup-links/configurations/query", {
      page: 0,
      pageSize: 50,
      includeInactive: false,
      search: name1,
    }, tenant);
    expect(queryAfter.json.items.some((i) => i.itemId === create1.json.itemId)).toBe(false);

    const queryInactive = await iamApi<List>(page, "POST", "/api/iam/signup-links/configurations/query", {
      page: 0,
      pageSize: 50,
      includeInactive: true,
      search: name1,
    }, tenant);
    const archived = queryInactive.json.items.find((i) => i.itemId === create1.json.itemId);
    expect(archived?.isActive).toBe(false);

    const getArchived = await iamApi<Config>(
      page,
      "GET",
      `/api/iam/signup-links/configurations/${create1.json.itemId}`,
      undefined,
      tenant,
    );
    expect(getArchived.status).toBe(200);
    expect(getArchived.json.isActive).toBe(false);

    // C2 — unregistered redirect
    const badRedirect = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...baseBody,
      name: uniqueName("E2E Bad Redirect"),
      redirectUri: "https://attacker.example.com/callback",
    }, tenant);
    expect(badRedirect.status).toBe(400);
    expect(JSON.stringify(badRedirect.json.errors || {})).toMatch(/RedirectUri/i);

    // C3 — unknown role
    const badRole = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...baseBody,
      name: uniqueName("E2E Bad Role"),
      defaultRoles: ["no-such-role-e2e"],
    }, tenant);
    expect(badRole.status).toBe(400);
    expect(JSON.stringify(badRole.json.errors || {})).toMatch(/DefaultRoles|Unknown role/i);

    // C4 — protocol-relative forwardedTo
    const badFwd = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...baseBody,
      name: uniqueName("E2E Bad Fwd"),
      defaultForwardedTo: "//evil.example.com",
    }, tenant);
    expect(badFwd.status).toBe(400);
    expect(JSON.stringify(badFwd.json.errors || {})).toMatch(/DefaultForwardedTo|ForwardedTo/i);

    // C1 — unknown client
    const badClient = await iamApi<Mutation>(page, "POST", "/api/iam/signup-links/configurations", {
      ...baseBody,
      name: uniqueName("E2E Bad Client"),
      clientId: "no-such-client-e2e",
    }, tenant);
    expect(badClient.status).toBe(400);
    expect(JSON.stringify(badClient.json.errors || {})).toMatch(/ClientId/i);

    // Cleanup remaining active configs created here
    for (const id of createdIds) {
      await iamApi(page, "POST", `/api/iam/signup-links/configurations/${id}/archive`, {}, tenant);
    }
  });

  test("C5 denied without manage-signup-links permission", async ({ page }) => {
    test.setTimeout(120_000);
    const permId = await ensureManageSignupLinksPermission(page);
    const tenant = await resolveTenantId(page);

    // Remove from clouduser
    const remove = await iamApi(page, "POST", "/api/iam/roles/assign-permissions", {
      slug: "clouduser",
      addPermissions: [],
      removePermissions: [permId],
    }, tenant);
    expect(remove.status).toBe(200);

    try {
      const denied = await iamApi(page, "POST", "/api/iam/signup-links/configurations/query", {
        page: 0,
        pageSize: 5,
        includeInactive: true,
      }, tenant);
      // ProtectedEndPoint → 403 (sometimes empty body)
      expect(denied.status).toBe(403);
    } finally {
      // Restore so later specs / humans are not locked out
      await iamApi(page, "POST", "/api/iam/roles/assign-permissions", {
        slug: "clouduser",
        addPermissions: [permId],
        removePermissions: [],
      }, tenant);
    }
  });
});
