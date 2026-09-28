import { test, expect } from "../../support/test-base";
import { ensureAuthenticated } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * #571 — POST iam/signup-links/summary against PR preview.
 * Counts-only aggregation per configuration; validation + C7 body shape.
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
type Summary = {
  configurationId: string;
  configurationName: string | null;
  fromUtc: string;
  toUtc: string;
  totalGenerated: number;
  used: number;
  neverUsed: number;
  neverUsedBreakdown: { active: number; expired: number; revoked: number };
  rejectedAttempts: number;
};
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

function uniqueEmail(prefix: string) {
  return `${prefix}.${Date.now()}.${Math.floor(Math.random() * 1e4)}@example.com`;
}

async function createActiveConfig(page: import("@playwright/test").Page, tenant: string) {
  const name = uniqueName("E2E Summary Cfg");
  const create = await iamApi<Mutation>(
    page,
    "POST",
    "/api/iam/signup-links/configurations",
    {
      name,
      description: "summary e2e",
      defaultRoles: [],
      defaultPermissions: [],
      clientId: IAM_CLIENT_ID,
      redirectUri: IAM_REDIRECT,
      credentialMode: "Passwordless",
      defaultLifetimeMinutes: 1440,
    },
    tenant
  );
  expect(create.status).toBe(200);
  expect(create.json.isSuccess).toBe(true);
  return { id: create.json.itemId!, name };
}

test.describe("Signup link summary (#571)", () => {
  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
  });

  test("H1/C6/C7 happy empty + C1–C3 validation + generate increments total", async ({
    page,
  }) => {
    const tenant = await resolveTenantId(page);
    await ensureManageSignupLinksPermission(page);
    const cfg = await createActiveConfig(page, tenant);

    // C1
    const missing = await iamApi(page, "POST", "/api/iam/signup-links/summary", {}, tenant);
    expect(missing.status).toBe(400);
    expect((missing.json as { errors: Record<string, string> }).errors.ConfigurationId).toBeTruthy();

    // C2
    const inverted = await iamApi(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      {
        configurationId: cfg.id,
        fromUtc: "2026-09-20T00:00:00Z",
        toUtc: "2026-09-10T00:00:00Z",
      },
      tenant
    );
    expect(inverted.status).toBe(400);
    expect((inverted.json as { errors: Record<string, string> }).errors.FromUtc).toBeTruthy();

    // C3 range
    const tooWide = await iamApi(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      {
        configurationId: cfg.id,
        fromUtc: new Date(Date.now() - 400 * 86400000).toISOString(),
        toUtc: new Date().toISOString(),
      },
      tenant
    );
    expect(tooWide.status).toBe(400);
    expect((tooWide.json as { errors: Record<string, string> }).errors.Range).toBeTruthy();

    // C3 future toUtc
    const future = await iamApi(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      {
        configurationId: cfg.id,
        fromUtc: new Date(Date.now() - 86400000).toISOString(),
        toUtc: new Date(Date.now() + 2 * 86400000).toISOString(),
      },
      tenant
    );
    expect(future.status).toBe(400);
    expect((future.json as { errors: Record<string, string> }).errors.ToUtc).toBeTruthy();

    // C6 empty with name
    const empty = await iamApi<Summary>(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      { configurationId: cfg.id },
      tenant
    );
    expect(empty.status).toBe(200);
    expect(empty.json.configurationId).toBe(cfg.id);
    expect(empty.json.configurationName).toBe(cfg.name);
    expect(empty.json.totalGenerated).toBe(0);
    expect(empty.json.used).toBe(0);
    expect(empty.json.neverUsed).toBe(0);
    expect(empty.json.neverUsedBreakdown).toEqual({ active: 0, expired: 0, revoked: 0 });
    expect(empty.json.rejectedAttempts).toBe(0);
    expect(empty.json.fromUtc).toBeTruthy();
    expect(empty.json.toUtc).toBeTruthy();

    // C7 — no link secrets in body
    const bodyKeys = Object.keys(empty.json).sort();
    expect(bodyKeys).toEqual(
      [
        "configurationId",
        "configurationName",
        "fromUtc",
        "toUtc",
        "totalGenerated",
        "used",
        "neverUsed",
        "neverUsedBreakdown",
        "rejectedAttempts",
      ].sort()
    );
    const raw = JSON.stringify(empty.json);
    expect(raw).not.toMatch(/codeHash|redirectUri|"email"|linkId/i);

    // Generate one link → totalGenerated 1, neverUsed.active 1
    const gen = await iamApi<Generate>(
      page,
      "POST",
      "/api/iam/signup-links",
      {
        configurationId: cfg.id,
        email: uniqueEmail("summary"),
        firstName: "Sum",
        lastName: "Mary",
      },
      tenant
    );
    expect(gen.status).toBe(200);
    expect(gen.json.linkId).toBeTruthy();

    const after = await iamApi<Summary>(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      { configurationId: cfg.id },
      tenant
    );
    expect(after.status).toBe(200);
    expect(after.json.totalGenerated).toBe(1);
    expect(after.json.used).toBe(0);
    expect(after.json.neverUsed).toBe(1);
    expect(after.json.neverUsedBreakdown.active).toBe(1);
    expect(after.json.used + after.json.neverUsed).toBe(after.json.totalGenerated);
    expect(
      after.json.neverUsedBreakdown.active +
        after.json.neverUsedBreakdown.expired +
        after.json.neverUsedBreakdown.revoked
    ).toBe(after.json.neverUsed);

    // C5 unknown id → zeros + null name
    const unknown = await iamApi<Summary>(
      page,
      "POST",
      "/api/iam/signup-links/summary",
      { configurationId: "does-not-exist-" + Date.now() },
      tenant
    );
    expect(unknown.status).toBe(200);
    expect(unknown.json.configurationName).toBeNull();
    expect(unknown.json.totalGenerated).toBe(0);

    // Cleanup archive
    await iamApi(page, "POST", `/api/iam/signup-links/configurations/${cfg.id}/archive`, {}, tenant);
  });
});
