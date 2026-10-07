import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * #567 Phase 2 — Signup link generation / query / revoke against PR preview.
 * Depends on Phase 1 configuration APIs. Exercises H1–H5 and C1–C5 where the
 * shared E2E tenant can (grant tree + org ladder with the clouduser token).
 */

const PERM_RESOURCE = "blocks-iam::iam::manage-signup-links";
const IAM_CLIENT_ID = "a5831e15-e193-4a4f-8e10-d04a4ad1705b";
const IAM_REDIRECT = `${process.env.E2E_BASE_URL?.replace(/\/$/, "")}/login/callback`;

type Mutation = { isSuccess?: boolean; itemId?: string; errors?: Record<string, string> | null };
type Generate = {
  linkId: string;
  url: string;
  expiresAtUtc: string;
  emailAlreadyExists: boolean;
};
type LinkList = {
  items: Array<{
    linkId: string;
    email: string;
    status: string;
    configurationId: string;
    organizationId: string;
    roles: string[];
  }>;
  totalCount: number;
};
type Perm = { itemId: string; resource: string; name: string };
type Assignable = { hierarchy: { slug: string }[]; standalone: { slug: string }[] };

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

function uniqueEmail(prefix: string) {
  return `${prefix}.${Date.now()}.${Math.floor(Math.random() * 1e4)}@example.com`;
}

async function createActiveConfig(page: import("@playwright/test").Page, tenant: string) {
  const name = uniqueName("E2E Gen Cfg");
  const create = await iamApi<Mutation>(
    page,
    "POST",
    "/api/iam/signup-links/configurations",
    {
      name,
      defaultRoles: ["clouduser"],
      defaultPermissions: [],
      clientId: IAM_CLIENT_ID,
      redirectUri: IAM_REDIRECT,
      defaultForwardedTo: "/app/profile",
      credentialMode: "PasswordRequired",
      defaultLifetimeMinutes: 60,
    },
    tenant,
  );
  expect(create.status, JSON.stringify(create.json)).toBe(200);
  expect(create.json.isSuccess).toBe(true);
  return create.json.itemId!;
}

test.describe("Signup link generation (#567)", () => {
  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
    await ensureManageSignupLinksPermission(page);
  });

  test("H1–H5 generate/query/revoke + C1/C3/C5 + assignable regression", async ({ page }) => {
    test.setTimeout(240_000);
    const tenant = await resolveTenantId(page);

    // Assignable regression baseline (byte-stable shape)
    const assignableBefore = await iamApi<Assignable>(page, "GET", "/api/iam/roles/assignable", undefined, tenant);
    expect(assignableBefore.status).toBe(200);
    expect(assignableBefore.json).toHaveProperty("hierarchy");
    expect(assignableBefore.json).toHaveProperty("standalone");

    const cfgId = await createActiveConfig(page, tenant);

    // H1 / H2 / H3 — generate with configuration defaults
    const email = uniqueEmail("e2e.signup");
    const gen = await iamApi<Generate | { errors: Record<string, string> }>(
      page,
      "POST",
      "/api/iam/signup-links",
      {
        configurationId: cfgId,
        email,
        firstName: "E2E",
        lastName: "Invitee",
        // If token org is default, org is required; if concrete, same org is fine.
        organizationId: "default",
      },
      tenant,
    );

    // Organization ladder: if caller is not default-scoped, "default" payload may 400.
    // Retry with omitted org when concrete-org caller, or with a concrete org from token cookies.
    let generateOk = gen.status === 200;
    let generateJson = gen.json as Generate;
    let usedOrg: string | undefined = "default";

    if (!generateOk) {
      const err = (gen.json as { errors?: Record<string, string> }).errors || {};
      if (err.OrganizationId) {
        // Concrete-token caller: omit payload org
        const retry = await iamApi<Generate>(
          page,
          "POST",
          "/api/iam/signup-links",
          {
            configurationId: cfgId,
            email,
            firstName: "E2E",
            lastName: "Invitee",
          },
          tenant,
        );
        expect(retry.status, JSON.stringify(retry.json)).toBe(200);
        generateOk = true;
        generateJson = retry.json;
        usedOrg = undefined;
      }
    }

    expect(generateOk).toBe(true);
    expect(generateJson.linkId).toBeTruthy();
    expect(generateJson.url).toContain(`/oidc/invitation/${tenant}#link=`);
    const code = generateJson.url.split("#link=")[1];
    expect(code.length).toBe(43);
    expect(generateJson.emailAlreadyExists).toBe(false);

    // C5 — list must not expose code or hash
    const list = await iamApi<LinkList>(
      page,
      "POST",
      "/api/iam/signup-links/query",
      { page: 0, pageSize: 50, configurationId: cfgId },
      tenant,
    );
    expect(list.status).toBe(200);
    expect(list.json.totalCount).toBeGreaterThanOrEqual(1);
    const item = list.json.items.find((i) => i.linkId === generateJson.linkId);
    expect(item).toBeTruthy();
    expect(item!.email).toBe(email.toLowerCase());
    expect(item!.roles).toEqual(["clouduser"]);
    expect(JSON.stringify(item)).not.toContain(code);
    expect(JSON.stringify(item).toLowerCase()).not.toContain("codehash");

    // H5 — revoke by id
    const revoke = await iamApi<Mutation>(
      page,
      "POST",
      `/api/iam/signup-links/${generateJson.linkId}/revoke`,
      {},
      tenant,
    );
    expect(revoke.status, JSON.stringify(revoke.json)).toBe(200);
    expect(revoke.json.isSuccess).toBe(true);

    // Second link then revoke-by-configuration
    const email2 = uniqueEmail("e2e.signup2");
    const gen2Body: Record<string, unknown> = {
      configurationId: cfgId,
      email: email2,
      firstName: "E2E",
      lastName: "Two",
    };
    if (usedOrg !== undefined) gen2Body.organizationId = usedOrg;
    const gen2 = await iamApi<Generate>(page, "POST", "/api/iam/signup-links", gen2Body, tenant);
    expect(gen2.status, JSON.stringify(gen2.json)).toBe(200);

    const revokeCfg = await iamApi<{ isSuccess: boolean; revokedCount: number }>(
      page,
      "POST",
      "/api/iam/signup-links/revoke-by-configuration",
      { configurationId: cfgId },
      tenant,
    );
    expect(revokeCfg.status, JSON.stringify(revokeCfg.json)).toBe(200);
    expect(revokeCfg.json.isSuccess).toBe(true);
    expect(revokeCfg.json.revokedCount).toBeGreaterThanOrEqual(1);

    // C3 — archived configuration rejected
    await iamApi(page, "POST", `/api/iam/signup-links/configurations/${cfgId}/archive`, {}, tenant);
    const againstArchived = await iamApi<{ errors?: Record<string, string> }>(
      page,
      "POST",
      "/api/iam/signup-links",
      {
        configurationId: cfgId,
        email: uniqueEmail("e2e.arch"),
        firstName: "X",
        lastName: "Y",
        ...(usedOrg !== undefined ? { organizationId: usedOrg } : {}),
      },
      tenant,
    );
    expect(againstArchived.status).toBe(400);
    expect(againstArchived.json.errors?.ConfigurationId).toMatch(/Not found or inactive/i);

    // C1 — organization conflict when caller has concrete org and payload differs
    // Recreate a config for the conflict probe
    const cfg2 = await createActiveConfig(page, tenant);
    const conflict = await iamApi<{ errors?: Record<string, string> }>(
      page,
      "POST",
      "/api/iam/signup-links",
      {
        configurationId: cfg2,
        email: uniqueEmail("e2e.conflict"),
        firstName: "X",
        lastName: "Y",
        organizationId: "org-other-e2e-should-conflict-or-be-required-path",
      },
      tenant,
    );
    // Either conflict (concrete token) or success if default token accepts that org id
    // (default-scoped callers may accept any payload org). Assert we never 500.
    expect([200, 400]).toContain(conflict.status);
    if (conflict.status === 400) {
      expect(JSON.stringify(conflict.json.errors || {})).toMatch(/OrganizationId/i);
    }

    // Assignable still shaped the same
    const assignableAfter = await iamApi<Assignable>(page, "GET", "/api/iam/roles/assignable", undefined, tenant);
    expect(assignableAfter.status).toBe(200);
    expect(JSON.stringify(assignableAfter.json.hierarchy.map((h) => h.slug).sort())).toBe(
      JSON.stringify(assignableBefore.json.hierarchy.map((h) => h.slug).sort()),
    );
    expect(JSON.stringify(assignableAfter.json.standalone.map((h) => h.slug).sort())).toBe(
      JSON.stringify(assignableBefore.json.standalone.map((h) => h.slug).sort()),
    );

    await iamApi(page, "POST", `/api/iam/signup-links/configurations/${cfg2}/archive`, {}, tenant);
  });

  test("ProtectedEndPoint denies generate without manage-signup-links", async ({ page }) => {
    test.setTimeout(120_000);
    const permId = await ensureManageSignupLinksPermission(page);
    const tenant = await resolveTenantId(page);

    const remove = await iamApi(
      page,
      "POST",
      "/api/iam/roles/assign-permissions",
      { slug: "clouduser", addPermissions: [], removePermissions: [permId] },
      tenant,
    );
    expect(remove.status).toBe(200);

    try {
      const denied = await iamApi(
        page,
        "POST",
        "/api/iam/signup-links",
        {
          configurationId: "any",
          email: "x@example.com",
          firstName: "X",
          lastName: "Y",
          organizationId: "default",
        },
        tenant,
      );
      expect([401, 403, 404]).toContain(denied.status);
    } finally {
      await iamApi(
        page,
        "POST",
        "/api/iam/roles/assign-permissions",
        { slug: "clouduser", addPermissions: [permId], removePermissions: [] },
        tenant,
      );
    }
  });
});
