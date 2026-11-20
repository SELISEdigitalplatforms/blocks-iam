import type { Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { test, expect } from "../../support/test-base";
import { ensureAuthenticated, loginThroughOidc } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * #683 — asynchronous batch user upsert: `POST iam/batch/users` and `GET iam/batch/jobs/{jobId}`
 * (spec section 10). Runs on deployed dev, where the Worker processes the jobs. Multi-organization is off on
 * the E2E tenant, so every membership is the `default` organization.
 *
 * Prerequisites owned by the spec author, never set up by this suite (it grants nothing and changes no role):
 * - A9: the E2E account's role holds `import-users`, `manage-user-roles`, `update-user-profiles`,
 *   `create-users` and `deactivate-users`;
 * - the role `e2e-batch-role` exists and the E2E account may grant it; `e2e-missing-role` does not exist;
 * - step 8 only: a second identity whose role `e2e-batch-limited` holds only `import-users`, passed as
 *   E2E_BATCH_LIMITED_USERNAME / E2E_BATCH_LIMITED_PASSWORD. Without it step 8 is covered by unit tests only.
 *
 * Every user created here has an `iam-batch-<runId>-…@yopmail.com` address, is created without mail, and is
 * deactivated at the end. Step 10 (performance, 1,000 users) runs only with E2E_BATCH_PERF=1.
 */

const BATCH = "/api/iam/batch/users";
const JOB = (id: string) => `/api/iam/batch/jobs/${id}`;
const BATCH_ROLE = "e2e-batch-role";
const MISSING_ROLE = "e2e-missing-role";
// The existing fixture's base role in `default`: the tenant's member role, which the E2E account holds.
const BASE_ROLE = process.env.E2E_BATCH_BASE_ROLE ?? "clouduser";
const FINAL = /^(Completed|CompletedWithErrors|Failed)$/;

type Submit = {
  isSuccess?: boolean;
  jobId?: string;
  status?: string;
  total?: number;
  rejected?: number;
  errors?: Record<string, string> | null;
};
type JobFailure = {
  ref: string;
  index: number;
  email?: string;
  errors: { field: string; code: string }[];
};
type Summary = { created: number; updated: number; unchanged: number; failed: number };
type Job = {
  jobId: string;
  status: string;
  total: number;
  summary: Summary;
  failures: JobFailure[];
  error?: string | null;
  createdDate?: string;
  completedAt?: string | null;
};
type Item = { ref: string; operation: "upsert"; data: Record<string, unknown> };
type UserData = Record<string, unknown>;

const runId = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const email = (n: string | number) => `iam-batch-${runId}-${n}@yopmail.com`;
const upsert = (ref: string, data: Record<string, unknown>): Item => ({
  ref,
  operation: "upsert",
  data,
});
const inDefault = (roles: string[]) => [{ organizationId: "default", roles, permissions: [] }];
const randomJobId = () => randomUUID().replace(/-/g, "");

function submit(page: Page, items: Item[], tenant: string) {
  return iamApi<Submit>(
    page,
    "POST",
    BATCH,
    { clientReference: `e2e-${runId}`, options: { sendEmail: false }, items },
    tenant,
  );
}

/** Polls the job until it reaches a final status (spec step 4: at most 60 s). */
async function waitForJob(
  page: Page,
  jobId: string,
  tenant: string,
  timeout = 60_000,
): Promise<Job> {
  let job: Job | undefined;
  await expect
    .poll(
      async () => {
        const res = await iamApi<Job>(page, "GET", JOB(jobId), undefined, tenant);
        expect(res.status, res.raw).toBe(200);
        job = res.json;
        return job.status;
      },
      { timeout, intervals: [500, 1_000, 2_000] },
    )
    .toMatch(FINAL);
  return job!;
}

/** The user with exactly this address, read through users/{id}; undefined when there is none. */
async function findUser(
  page: Page,
  address: string,
  tenant: string,
): Promise<UserData | undefined> {
  const list = await iamApi<{ data?: UserData[] }>(
    page,
    "POST",
    "/api/iam/users",
    { page: 1, pageSize: 10, filter: { email: address, name: "" } },
    tenant,
  );
  expect(list.status, list.raw).toBe(200);
  const hit = (list.json.data ?? []).find(
    (u) => String(u.email ?? u.Email).toLowerCase() === address.toLowerCase(),
  );
  if (!hit) return undefined;
  const user = await iamApi<{ data?: UserData }>(
    page,
    "GET",
    `/api/iam/users/${hit.itemId ?? hit.ItemId}`,
    undefined,
    tenant,
  );
  expect(user.status, user.raw).toBe(200);
  return user.json.data;
}

/** Ids of every user this run created (the email filter matches substrings). */
async function runUserIds(
  page: Page,
  tenant: string,
  prefix = `iam-batch-${runId}-`,
): Promise<string[]> {
  const ids: string[] = [];
  for (let pageNo = 1; ; pageNo++) {
    const list = await iamApi<{ data?: UserData[] }>(
      page,
      "POST",
      "/api/iam/users",
      { page: pageNo, pageSize: 100, filter: { email: prefix, name: "" } },
      tenant,
    );
    expect(list.status, list.raw).toBe(200);
    const rows = list.json.data ?? [];
    ids.push(...rows.map((u) => String(u.itemId ?? u.ItemId)));
    if (rows.length < 100) return [...new Set(ids)];
  }
}

const rolesOf = (u: UserData | undefined) => [...((u?.roles ?? []) as string[])].sort();

async function createFixture(page: Page, address: string, tenant: string): Promise<string> {
  const res = await iamApi<{ isSuccess?: boolean }>(
    page,
    "POST",
    "/api/iam/users/create",
    {
      email: address,
      firstName: "Batch",
      lastName: "Existing",
      organizationId: "default",
      roles: [BASE_ROLE],
      permissions: [],
      sendMail: false,
      notifyUser: false,
    },
    tenant,
  );
  expect(res.status, res.raw).toBe(200);
  expect(res.json.isSuccess, res.raw).toBe(true);
  const user = await findUser(page, address, tenant);
  expect(user, "fixture user").toBeTruthy();
  expect(rolesOf(user)).toEqual([BASE_ROLE]);
  return String(user!.itemId);
}

/** Cleanup (spec section 10): the fixture keeps only its base role; every user of this run is deactivated. */
async function cleanUp(page: Page, tenant: string, fixtureId: string | undefined, prefix?: string) {
  if (fixtureId) {
    await iamApi(
      page,
      "POST",
      "/api/iam/users/access",
      {
        userId: fixtureId,
        organizationId: "default",
        roles: [BASE_ROLE],
        permissions: [],
        notifyUser: false,
      },
      tenant,
    );
  }
  for (const id of await runUserIds(page, tenant, prefix)) {
    const res = await iamApi<{ isSuccess?: boolean }>(
      page,
      "POST",
      "/api/iam/users/deactivate",
      { userId: id },
      tenant,
    );
    expect(res.status, `deactivate ${id}: ${res.raw}`).toBe(200);
  }
}

/** Prerequisites the spec author owns (A9 and the fixture role). Reads only. */
async function expectPrerequisites(page: Page, tenant: string) {
  const job = await iamApi(page, "GET", JOB(randomJobId()), undefined, tenant);
  expect(job.status, "import-users must be assigned to the E2E account's role (#683 A9)").not.toBe(
    403,
  );
  const roles = await iamApi<{ data?: { slug: string }[] }>(
    page,
    "POST",
    "/api/iam/roles",
    { page: 0, pageSize: 300 },
    tenant,
  );
  expect(roles.status, roles.raw).toBe(200);
  const slugs = (roles.json.data ?? []).map((r) => r.slug);
  expect(slugs, `role ${BATCH_ROLE} must exist (#683 section 10 prerequisite)`).toContain(
    BATCH_ROLE,
  );
  expect(slugs, `role ${MISSING_ROLE} must not exist`).not.toContain(MISSING_ROLE);
}

const p95 = (values: number[]) =>
  [...values].sort((a, b) => a - b)[Math.ceil(values.length * 0.95) - 1];

test.describe("Batch user upsert (#683)", () => {
  test.describe.configure({ mode: "serial" });

  test.beforeEach(async ({ page }) => {
    await ensureAuthenticated(page);
  });

  // Steps 1, 2 and 9 write nothing and need no Worker, so they also run on PR previews.
  test("steps 1, 2, 9: request-level checks and an unknown job (C1, C18)", async ({ page }) => {
    const tenant = await resolveTenantId(page);

    await test.step("[Negative] step 1 / C1: 101 items → 400 items_limit_exceeded", async () => {
      const items = Array.from({ length: 101 }, (_, i) =>
        upsert(`over-${i}`, { email: email(`over-${i}`) }),
      );
      const res = await submit(page, items, tenant);
      expect(res.status, res.raw).toBe(400);
      expect(res.json.isSuccess).toBe(false);
      expect(res.json.errors).toHaveProperty("items_limit_exceeded");
      expect(res.json.jobId ?? null).toBeNull();
    });

    await test.step("[Negative] step 2 / C1: emails that differ only by case → 400 duplicate_email", async () => {
      const res = await submit(
        page,
        [
          upsert("dup-a", { email: email("dup") }),
          upsert("dup-b", { email: email("dup").toUpperCase() }),
        ],
        tenant,
      );
      expect(res.status, res.raw).toBe(400);
      expect(res.json.errors).toHaveProperty("duplicate_email");
      expect(res.json.jobId ?? null).toBeNull();
    });

    await test.step("[Negative] step 9 / C18: an unknown job id → 404", async () => {
      const res = await iamApi<{ isSuccess?: boolean; errors?: Record<string, string> }>(
        page,
        "GET",
        JOB(randomJobId()),
        undefined,
        tenant,
      );
      expect(res.status, res.raw).toBe(404);
      expect(res.json.isSuccess).toBe(false);
      expect(res.json.errors).toHaveProperty("jobId");
    });

    // Nothing above may have written a user.
    expect(await runUserIds(page, tenant)).toEqual([]);
  });

  test("steps 3–7: job result, users written, resend unchanged, profile update (H1–H6, H10–H13, C3, C5, C11)", async ({
    page,
  }) => {
    test.setTimeout(300_000);
    const tenant = await resolveTenantId(page);
    await expectPrerequisites(page, tenant);

    const existingEmail = email("existing");
    let fixtureId: string | undefined;
    try {
      fixtureId = await createFixture(page, existingEmail, tenant);

      // Step 3's items by index: 0 new-1, 1 new-2, 2 existing, 3 unknown role, 4 invalid email.
      const validItems = [
        upsert("new-1", { email: email(1), firstName: "Batch", lastName: "One", sendEmail: false }),
        upsert("new-2", {
          email: email(2),
          firstName: "Batch",
          lastName: "Two",
          memberships: inDefault([BATCH_ROLE]),
        }),
        upsert("existing", {
          email: existingEmail,
          firstName: "Batch",
          lastName: "Existing",
          memberships: inDefault([BATCH_ROLE]),
        }),
      ];
      let jobId = "";

      await test.step("[Positive] step 3 / H1, C3: five items → 202 Queued with one rejected", async () => {
        const res = await submit(
          page,
          [
            ...validItems,
            upsert("bad", { email: email("bad"), memberships: inDefault([MISSING_ROLE]) }),
            upsert("invalid", { email: "x" }),
          ],
          tenant,
        );
        expect(res.status, res.raw).toBe(202);
        expect(res.json.isSuccess).toBe(true);
        expect(res.json.status).toBe("Queued");
        expect(res.json.total).toBe(5);
        expect(res.json.rejected).toBe(1);
        expect(res.json.jobId).toBeTruthy();
        jobId = res.json.jobId!;
      });

      await test.step("[Positive] step 4 / H11, H13, C5: CompletedWithErrors, failures ordered by index", async () => {
        const job = await waitForJob(page, jobId, tenant);
        expect(job.status).toBe("CompletedWithErrors");
        expect(job.total).toBe(5);
        expect(job.summary).toEqual({ created: 2, updated: 1, unchanged: 0, failed: 2 });
        expect(job.failures.map((f) => [f.index, f.ref])).toEqual([
          [3, "bad"],
          [4, "invalid"],
        ]);
        expect(job.failures[0].errors.map((e) => e.code)).toContain("role_not_found");
        expect(job.failures[1].errors.map((e) => e.code)).toContain("email_invalid");
        expect(job.completedAt).toBeTruthy();
      });

      await test.step("[Positive] step 5 / H2, H4, C11: pending users with the expected roles", async () => {
        const one = await findUser(page, email(1), tenant);
        const two = await findUser(page, email(2), tenant);
        expect(one, "new-1").toBeTruthy();
        expect(two, "new-2").toBeTruthy();
        for (const u of [one!, two!]) {
          expect(u.isVerified).toBe(false);
          expect(u.organizationIds).toEqual(["default"]);
        }
        // No memberships sent with multi-organization off: `default`, no roles.
        expect(rolesOf(one)).toEqual([]);
        expect(rolesOf(two)).toEqual([BATCH_ROLE]);
        // Add-only merge: the base role stays and the batch role is added.
        expect(rolesOf(await findUser(page, existingEmail, tenant))).toEqual(
          [BASE_ROLE, BATCH_ROLE].sort(),
        );
        expect(await findUser(page, email("bad"), tenant)).toBeUndefined();
      });

      await test.step("[Positive] step 6 / H10: resending the valid items changes nothing", async () => {
        const res = await submit(page, validItems, tenant);
        expect(res.status, res.raw).toBe(202);
        expect(res.json.rejected).toBe(0);
        const job = await waitForJob(page, res.json.jobId!, tenant);
        expect(job.status).toBe("Completed");
        expect(job.summary).toEqual({ created: 0, updated: 0, unchanged: 3, failed: 0 });
        expect(await runUserIds(page, tenant)).toHaveLength(3);
      });

      await test.step("[Positive] step 7 / H6: a changed firstName updates the existing user", async () => {
        const res = await submit(
          page,
          [
            upsert("existing", {
              email: existingEmail,
              firstName: "Renamed",
              lastName: "Existing",
            }),
          ],
          tenant,
        );
        expect(res.status, res.raw).toBe(202);
        const job = await waitForJob(page, res.json.jobId!, tenant);
        expect(job.status).toBe("Completed");
        expect(job.summary).toEqual({ created: 0, updated: 1, unchanged: 0, failed: 0 });
        const after = await findUser(page, existingEmail, tenant);
        expect(after?.firstName).toBe("Renamed");
        expect(rolesOf(after)).toEqual([BASE_ROLE, BATCH_ROLE].sort());
      });
    } finally {
      await cleanUp(page, tenant, fixtureId);
    }
  });

  test("step 8: an identity without manage-user-roles cannot add a role (C8)", async ({
    page,
    browser,
  }) => {
    const limited = {
      email: process.env.E2E_BATCH_LIMITED_USERNAME ?? "",
      password: process.env.E2E_BATCH_LIMITED_PASSWORD ?? "",
    };
    test.skip(
      !limited.email || !limited.password,
      "needs the second identity with role e2e-batch-limited (E2E_BATCH_LIMITED_USERNAME/PASSWORD); covered by unit tests otherwise",
    );
    test.setTimeout(240_000);
    const tenant = await resolveTenantId(page);
    await expectPrerequisites(page, tenant);

    const prefix = `iam-batch-${runId}-limited-`;
    const fixtureEmail = `${prefix}existing@yopmail.com`;
    let fixtureId: string | undefined;
    const context = await browser.newContext();
    try {
      fixtureId = await createFixture(page, fixtureEmail, tenant);
      const other = await context.newPage();
      await loginThroughOidc(other, { credentials: limited });
      const res = await submit(
        other,
        [upsert("existing", { email: fixtureEmail, memberships: inDefault([BATCH_ROLE]) })],
        tenant,
      );
      expect(res.status, res.raw).toBe(202);
      const job = await waitForJob(other, res.json.jobId!, tenant);
      expect(job.status).toBe("CompletedWithErrors");
      expect(job.summary).toEqual({ created: 0, updated: 0, unchanged: 0, failed: 1 });
      expect(job.failures[0].errors.map((e) => e.code)).toContain("manage_user_roles_required");
      expect(rolesOf(await findUser(page, fixtureEmail, tenant))).toEqual([BASE_ROLE]);
    } finally {
      await context.close();
      await cleanUp(page, tenant, fixtureId, prefix);
    }
  });

  test("step 10: 10 × 100 new users, submit and job duration p95 (H15)", async ({ page }) => {
    test.skip(
      process.env.E2E_BATCH_PERF !== "1",
      "creates 1,000 users on the tenant; set E2E_BATCH_PERF=1 to run",
    );
    test.setTimeout(1_800_000);
    const tenant = await resolveTenantId(page);
    await expectPrerequisites(page, tenant);

    const prefix = `iam-batch-${runId}-perf-`;
    const submitMs: number[] = [];
    const jobMs: number[] = [];
    try {
      for (let run = 0; run < 10; run++) {
        const items = Array.from({ length: 100 }, (_, i) =>
          upsert(`perf-${run}-${i}`, {
            email: `${prefix}${run}-${i}@yopmail.com`,
            memberships: inDefault([BATCH_ROLE]),
          }),
        );
        const started = Date.now();
        const res = await submit(page, items, tenant);
        submitMs.push(Date.now() - started);
        expect(res.status, res.raw).toBe(202);
        // One job at a time, so each one runs on an idle queue.
        const job = await waitForJob(page, res.json.jobId!, tenant, 120_000);
        expect(job.status).toBe("Completed");
        expect(job.summary.created).toBe(100);
        jobMs.push(Date.parse(job.completedAt!) - Date.parse(job.createdDate!));
      }
      const result = { submitMs, jobMs, submitP95: p95(submitMs), jobP95: p95(jobMs) };
      test.info().annotations.push({ type: "H15", description: JSON.stringify(result) });
      expect(result.submitP95, "submit p95 ≤ 1 s").toBeLessThanOrEqual(1_000);
      expect(result.jobP95, "job p95 ≤ 10 s").toBeLessThanOrEqual(10_000);
    } finally {
      await cleanUp(page, tenant, undefined, prefix);
    }
  });
});
