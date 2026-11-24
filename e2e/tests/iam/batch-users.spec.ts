import type { Page } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { test, expect } from "../../support/test-base";
import { ensureAuthenticated, loginThroughOidc } from "../../support/login-helper";
import { iamApi, resolveTenantId } from "../../support/iam-api";

/**
 * #683 — asynchronous batch user upsert: `POST iam/batch/users` and `GET iam/batch/jobs/{jobId}`
 * (spec section 10), on the contract after #690: flat items (`ref` optional, no `operation` / `data`
 * wrapper, no `options.sendEmail`), per-item `mail`, and a separate mail phase reported on the job.
 * Runs on deployed dev, where the Worker processes the jobs. Multi-organization is off on the E2E
 * tenant, so every membership is the `default` organization.
 *
 * Tenant prerequisites, never set up by this suite (it creates no role and changes no role):
 * - A9: the E2E account's role holds `import-users`, `manage-user-roles`, `update-user-profiles`,
 *   `create-users` and `deactivate-users` (on dev: `clouduser`);
 * - the role `e2e-batch-role` exists (no permissions; the role the batch grants) and `e2e-batch-limited`
 *   exists (only `import-users`; the fixture's base role and step 8's role); `e2e-missing-role` does not;
 * - step 8 only: a second identity whose only role is `e2e-batch-limited`, passed as
 *   E2E_BATCH_LIMITED_USERNAME / E2E_BATCH_LIMITED_PASSWORD. Without it step 8 is skipped and stays
 *   covered by unit tests (`BatchUserServiceProcessTests`).
 *
 * Mail: no item names a template that exists, so the mail phase queues nothing and no mail is sent. Every
 * mail request ends as a reported skip (`template_required`, `no_new_membership`, `import_failed`,
 * `template_not_found`, `user_exists`).
 *
 * Every user created here has an `iam-batch-<runId>-…@yopmail.com` address and is deactivated at the end.
 * Step 10 (performance, 10 × 100 users) runs only with E2E_BATCH_PERF=1.
 */

const BATCH = "/api/iam/batch/users";
const JOB = (id: string) => `/api/iam/batch/jobs/${id}`;
const BATCH_ROLE = "e2e-batch-role";
const LIMITED_ROLE = "e2e-batch-limited";
const MISSING_ROLE = "e2e-missing-role";
// The existing fixture's base role in `default`: the least-privileged fixture role (only import-users).
const BASE_ROLE = process.env.E2E_BATCH_BASE_ROLE ?? LIMITED_ROLE;
const FINAL = /^(Completed|CompletedWithErrors|Failed)$/;

type Submit = {
  isSuccess?: boolean;
  jobId?: string | null;
  status?: string | null;
  total?: number;
  rejected?: number;
  errors?: Record<string, string> | null;
};
type JobFailure = {
  ref?: string | null;
  index: number;
  email?: string | null;
  errors: { field: string; code: string; message: string }[];
};
type MailResult = {
  index: number;
  ref?: string | null;
  email?: string | null;
  outcome: string;
  code: string;
  template?: string | null;
  language?: string | null;
};
type JobMail = {
  status: string;
  requested: number;
  queued: number;
  skipped: number;
  failed: number;
  results: MailResult[];
  error?: string | null;
};
type Summary = { created: number; updated: number; unchanged: number; failed: number };
type Job = {
  isSuccess: boolean;
  jobId: string;
  resource: string;
  clientReference?: string | null;
  status: string;
  total: number;
  summary: Summary;
  failures: JobFailure[];
  error?: string | null;
  mail?: JobMail | null;
  createdDate?: string;
  startedAt?: string | null;
  completedAt?: string | null;
};
type Mail = { send: boolean; template?: string; existingUserTemplate?: string; language?: string };
type Item = {
  ref?: string;
  email?: string;
  firstName?: string;
  lastName?: string;
  memberships?: { organizationId: string; roles: string[]; permissions: string[] }[];
  mail?: Mail;
  [legacy: string]: unknown;
};
type UserData = Record<string, unknown>;

const runId = `${Date.now().toString(36)}${Math.floor(Math.random() * 1e4)}`;
const email = (n: string | number) => `iam-batch-${runId}-${n}@yopmail.com`;
const inDefault = (roles: string[]) => [{ organizationId: "default", roles, permissions: [] }];
const randomJobId = () => randomUUID().replace(/-/g, "");
// A template name no tenant has: the mail phase must skip it, so nothing is ever sent.
const MISSING_TEMPLATE = `e2e-missing-template-${runId}`;

function submit(page: Page, items: Item[], tenant: string, clientReference = `e2e-${runId}`) {
  return iamApi<Submit>(page, "POST", BATCH, { clientReference, items }, tenant);
}

async function readJob(page: Page, jobId: string, tenant: string): Promise<Job> {
  const res = await iamApi<Job>(page, "GET", JOB(jobId), undefined, tenant);
  expect(res.status, res.raw).toBe(200);
  return res.json;
}

/**
 * Polls the job until the import reaches a final status (spec step 4: at most 60 s) and, when the job
 * has a mail phase, until that phase is final too.
 */
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
        job = await readJob(page, jobId, tenant);
        const mailDone = !job.mail || FINAL.test(job.mail.status);
        return FINAL.test(job.status) && mailDone ? "final" : `${job.status}/${job.mail?.status}`;
      },
      { timeout, intervals: [500, 1_000, 2_000] },
    )
    .toBe("final");
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
    { page: 0, pageSize: 10, filter: { email: address, name: "" } },
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
  // The users list pages from 0 (Skip = PageSize * Page).
  for (let pageNo = 0; ; pageNo++) {
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

/** Roles in `default`: the users API returns roles keyed by organization. */
function rolesOf(u: UserData | undefined): string[] {
  const roles = u?.roles;
  const list = Array.isArray(roles)
    ? roles
    : ((roles as Record<string, string[]> | undefined)?.default ?? []);
  return [...(list as string[])].sort();
}

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

/**
 * Cleanup (spec section 10): the fixture keeps only its base role; every user of this run is deactivated.
 * Never throws: it returns what failed, so a cleanup problem cannot hide the test's own failure.
 */
async function cleanUp(
  page: Page,
  tenant: string,
  fixtureId: string | undefined,
  prefix?: string,
): Promise<string[]> {
  const problems: string[] = [];
  try {
    if (fixtureId) {
      const reset = await iamApi(
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
      if (reset.status !== 200)
        problems.push(`users/access ${fixtureId}: ${reset.status} ${reset.raw}`);
    }
    for (const id of await runUserIds(page, tenant, prefix)) {
      const res = await iamApi(page, "POST", "/api/iam/users/deactivate", { userId: id }, tenant);
      if (res.status !== 200) problems.push(`deactivate ${id}: ${res.status} ${res.raw}`);
    }
  } catch (error) {
    problems.push(`cleanup: ${String(error)}`);
  }
  return problems;
}

/** Runs the cleanup after a test body; fails the test on a cleanup problem only when the body passed. */
async function finish(
  bodyPassed: boolean,
  page: Page,
  tenant: string,
  fixtureId: string | undefined,
  prefix?: string,
) {
  const problems = await cleanUp(page, tenant, fixtureId, prefix);
  if (problems.length === 0) return;
  test.info().annotations.push({ type: "cleanup", description: problems.join("; ") });
  if (bodyPassed) throw new Error(`Cleanup failed: ${problems.join("; ")}`);
}

/** Tenant prerequisites (A9 and the fixture roles). Reads only. */
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
  for (const slug of [BATCH_ROLE, LIMITED_ROLE, BASE_ROLE]) {
    expect(slugs, `role ${slug} must exist (#683 section 10 prerequisite)`).toContain(slug);
  }
  expect(slugs, `role ${MISSING_ROLE} must not exist`).not.toContain(MISSING_ROLE);
}

const codesOf = (f: JobFailure | undefined) => (f?.errors ?? []).map((e) => e.code);
const mailSkips = (mail: JobMail | null | undefined) =>
  (mail?.results ?? []).map((r) => [r.index, r.outcome, r.code]);
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
      const items = Array.from({ length: 101 }, (_, i) => ({
        ref: `over-${i}`,
        email: email(`over-${i}`),
      }));
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
          { ref: "dup-a", email: email("dup") },
          { ref: "dup-b", email: email("dup").toUpperCase() },
        ],
        tenant,
      );
      expect(res.status, res.raw).toBe(400);
      expect(res.json.errors).toHaveProperty("duplicate_email");
      expect(res.json.errors?.duplicate_email).toContain(email("dup"));
      expect(res.json.jobId ?? null).toBeNull();
    });

    await test.step("[Negative] C1: no items, and a repeated ref → 400", async () => {
      const empty = await submit(page, [], tenant);
      expect(empty.status, empty.raw).toBe(400);
      expect(empty.json.errors).toHaveProperty("items_required");

      const dupRef = await submit(
        page,
        [
          { ref: "same", email: email("ref-a") },
          { ref: "same", email: email("ref-b") },
        ],
        tenant,
      );
      expect(dupRef.status, dupRef.raw).toBe(400);
      expect(dupRef.json.errors).toHaveProperty("duplicate_ref");
    });

    await test.step("[Negative] client_reference_too_long → 400", async () => {
      const res = await submit(page, [{ email: email("cref") }], tenant, "x".repeat(201));
      expect(res.status, res.raw).toBe(400);
      expect(res.json.errors).toHaveProperty("client_reference_too_long");
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

  test("C3, C4, #690 flat payload: every item rejected → 202 CompletedWithErrors without the Worker", async ({
    page,
  }) => {
    const tenant = await resolveTenantId(page);
    // Item 0 uses the pre-#690 wrapper ({ref, operation, data}): its fields are unknown now, so it has no
    // email. Item 1 is flat with an invalid email; item 2 has a phone without "+".
    const res = await submit(
      page,
      [
        { ref: "legacy", operation: "upsert", data: { email: email("legacy") } },
        { ref: "invalid", email: "not-an-email" },
        { email: email("phone"), phoneNumber: "0170000000" },
      ],
      tenant,
    );
    expect(res.status, res.raw).toBe(202);
    expect(res.json.isSuccess).toBe(true);
    expect(res.json.status).toBe("CompletedWithErrors");
    expect(res.json.total).toBe(3);
    expect(res.json.rejected).toBe(3);

    // Saved directly as final (no queue message), so it is readable at once.
    const job = await readJob(page, res.json.jobId!, tenant);
    expect(job.status).toBe("CompletedWithErrors");
    expect(job.resource).toBe("users");
    expect(job.clientReference).toBe(`e2e-${runId}`);
    expect(job.summary).toEqual({ created: 0, updated: 0, unchanged: 0, failed: 3 });
    expect(job.failures.map((f) => [f.index, f.ref ?? null])).toEqual([
      [0, "legacy"],
      [1, "invalid"],
      [2, null],
    ]);
    expect(job.failures[0].errors).toEqual([
      expect.objectContaining({ field: "email", code: "email_required" }),
    ]);
    expect(job.failures[1].errors).toEqual([
      expect.objectContaining({ field: "email", code: "email_invalid" }),
    ]);
    expect(job.failures[2].errors).toEqual([
      expect.objectContaining({ field: "phoneNumber", code: "phone_invalid" }),
    ]);
    expect(job.completedAt).toBeTruthy();
    expect(job.mail ?? null).toBeNull();
    expect(await runUserIds(page, tenant)).toEqual([]);
  });

  test("steps 3–7: job result, users written, mail phase, resend unchanged, profile update (H1–H6, H10–H13, C3, C5, C11)", async ({
    page,
  }) => {
    test.setTimeout(300_000);
    const tenant = await resolveTenantId(page);
    await expectPrerequisites(page, tenant);

    const existingEmail = email("existing");
    let fixtureId: string | undefined;
    let passed = false;
    try {
      fixtureId = await createFixture(page, existingEmail, tenant);

      // Step 3's items by index: 0 new-1, 1 new-2, 2 existing, 3 unknown role, 4 invalid email, 5 new-3.
      const newOne: Item = { ref: "new-1", email: email(1), firstName: "Batch", lastName: "One" };
      const newTwo: Item = {
        ref: "new-2",
        email: email(2),
        firstName: "Batch",
        lastName: "Two",
        memberships: inDefault([BATCH_ROLE]),
        // send without a template: the user is imported, the mail skipped (template_required).
        mail: { send: true },
      };
      const existing: Item = {
        ref: "existing",
        email: existingEmail.toUpperCase(),
        firstName: "Batch",
        lastName: "Existing",
        memberships: inDefault([BATCH_ROLE]),
        // An existing user with no new organization: skipped (no_new_membership).
        mail: { send: true, existingUserTemplate: MISSING_TEMPLATE },
      };
      const newThree: Item = {
        ref: "new-3",
        email: email(3),
        mail: { send: true, template: MISSING_TEMPLATE, language: "en-US" },
      };
      let jobId = "";

      await test.step("[Positive] step 3 / H1, C3: six items → 202 Queued with one rejected", async () => {
        const res = await submit(
          page,
          [
            newOne,
            newTwo,
            existing,
            {
              ref: "bad",
              email: email("bad"),
              memberships: inDefault([MISSING_ROLE]),
              mail: { send: true, template: MISSING_TEMPLATE },
            },
            { ref: "invalid", email: "x" },
            newThree,
          ],
          tenant,
        );
        expect(res.status, res.raw).toBe(202);
        expect(res.json.isSuccess).toBe(true);
        expect(res.json.status).toBe("Queued");
        expect(res.json.total).toBe(6);
        expect(res.json.rejected).toBe(1);
        expect(res.json.jobId).toBeTruthy();
        jobId = res.json.jobId!;
      });

      await test.step("[Positive] step 4 / H11, H13, C5: CompletedWithErrors, failures ordered by index", async () => {
        const job = await waitForJob(page, jobId, tenant);
        expect(job.status).toBe("CompletedWithErrors");
        expect(job.total).toBe(6);
        expect(job.error ?? null).toBeNull();
        expect(job.summary).toEqual({ created: 3, updated: 1, unchanged: 0, failed: 2 });
        expect(job.failures.map((f) => [f.index, f.ref])).toEqual([
          [3, "bad"],
          [4, "invalid"],
        ]);
        expect(job.failures[0].email).toBe(email("bad"));
        expect(job.failures[0].errors).toContainEqual(
          expect.objectContaining({ field: "memberships[0].roles[0]", code: "role_not_found" }),
        );
        expect(codesOf(job.failures[1])).toEqual(["email_invalid"]);
        expect(job.startedAt).toBeTruthy();
        expect(job.completedAt).toBeTruthy();
        expect(Date.parse(job.completedAt!)).toBeGreaterThanOrEqual(Date.parse(job.createdDate!));
      });

      await test.step("[Positive] #690 mail phase: every requested mail is accounted for, none is sent", async () => {
        const job = await readJob(page, jobId, tenant);
        expect(job.mail, "the job has a mail phase").toBeTruthy();
        expect(job.mail!.status).toBe("Completed");
        expect(job.mail!.requested).toBe(4);
        expect(job.mail!.queued).toBe(0);
        expect(job.mail!.failed).toBe(0);
        expect(job.mail!.skipped).toBe(4);
        expect(mailSkips(job.mail)).toEqual([
          [1, "Skipped", "template_required"],
          [2, "Skipped", "no_new_membership"],
          [3, "Skipped", "import_failed"],
          [5, "Skipped", "template_not_found"],
        ]);
        const notFound = job.mail!.results.find((r) => r.index === 5)!;
        expect(notFound.ref).toBe("new-3");
        expect(notFound.template).toBe(MISSING_TEMPLATE);
        expect(notFound.language).toBe("en-US");
      });

      await test.step("[Positive] step 5 / H2, H4, C11: pending users with the expected roles", async () => {
        const one = await findUser(page, email(1), tenant);
        const two = await findUser(page, email(2), tenant);
        const three = await findUser(page, email(3), tenant);
        for (const [name, u] of [
          ["new-1", one],
          ["new-2", two],
          ["new-3", three],
        ] as const) {
          expect(u, name).toBeTruthy();
          expect(u!.isVerified, name).toBe(false);
          expect(u!.organizationIds ?? ["default"], name).toEqual(["default"]);
        }
        // No memberships sent with multi-organization off: `default`, no roles.
        expect(rolesOf(one)).toEqual([]);
        expect(rolesOf(two)).toEqual([BATCH_ROLE]);
        expect(one!.firstName).toBe("Batch");
        expect(one!.lastName).toBe("One");
        // Add-only merge: the base role stays and the batch role is added; the email matched case-insensitively.
        const after = await findUser(page, existingEmail, tenant);
        expect(after!.itemId).toBe(fixtureId);
        expect(rolesOf(after)).toEqual([BASE_ROLE, BATCH_ROLE].sort());
        expect(await findUser(page, email("bad"), tenant)).toBeUndefined();
        expect(await findUser(page, "x", tenant)).toBeUndefined();
      });

      await test.step("[Positive] step 6 / H10, #690: resending the valid items changes nothing and mails nobody", async () => {
        const res = await submit(page, [newOne, newTwo, existing, newThree], tenant);
        expect(res.status, res.raw).toBe(202);
        expect(res.json.rejected).toBe(0);
        const job = await waitForJob(page, res.json.jobId!, tenant);
        expect(job.status).toBe("Completed");
        expect(job.failures).toEqual([]);
        expect(job.summary).toEqual({ created: 0, updated: 0, unchanged: 4, failed: 0 });
        expect(job.mail!.status).toBe("Completed");
        expect(job.mail!.queued).toBe(0);
        // The users now exist: new users' templates no longer apply, and nobody joined a new organization.
        expect(mailSkips(job.mail)).toEqual([
          [1, "Skipped", "user_exists"],
          [2, "Skipped", "no_new_membership"],
          [3, "Skipped", "user_exists"],
        ]);
        expect(await runUserIds(page, tenant)).toHaveLength(4);
      });

      await test.step("[Positive] step 7 / H6: a changed firstName updates the existing user", async () => {
        const res = await submit(
          page,
          [{ ref: "existing", email: existingEmail, firstName: "Renamed", lastName: "Existing" }],
          tenant,
        );
        expect(res.status, res.raw).toBe(202);
        const job = await waitForJob(page, res.json.jobId!, tenant);
        expect(job.status).toBe("Completed");
        expect(job.summary).toEqual({ created: 0, updated: 1, unchanged: 0, failed: 0 });
        expect(job.mail ?? null).toBeNull();
        const after = await findUser(page, existingEmail, tenant);
        expect(after?.firstName).toBe("Renamed");
        expect(rolesOf(after)).toEqual([BASE_ROLE, BATCH_ROLE].sort());
      });
      passed = true;
    } finally {
      await finish(passed, page, tenant, fixtureId);
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
    let passed = false;
    try {
      fixtureId = await createFixture(page, fixtureEmail, tenant);
      const other = await context.newPage();
      await loginThroughOidc(other, { credentials: limited });
      const res = await submit(
        other,
        [{ ref: "existing", email: fixtureEmail, memberships: inDefault([BATCH_ROLE]) }],
        tenant,
      );
      expect(res.status, res.raw).toBe(202);
      const job = await waitForJob(other, res.json.jobId!, tenant);
      expect(job.status).toBe("CompletedWithErrors");
      expect(job.summary).toEqual({ created: 0, updated: 0, unchanged: 0, failed: 1 });
      expect(codesOf(job.failures[0])).toContain("manage_user_roles_required");
      expect(rolesOf(await findUser(page, fixtureEmail, tenant))).toEqual([BASE_ROLE]);
      passed = true;
    } finally {
      await context.close();
      await finish(passed, page, tenant, fixtureId, prefix);
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

    const runs = Number(process.env.E2E_BATCH_PERF_RUNS ?? 10);
    const prefix = `iam-batch-${runId}-perf-`;
    const submitMs: number[] = [];
    const jobMs: number[] = [];
    let passed = false;
    try {
      for (let run = 0; run < runs; run++) {
        const items = Array.from({ length: 100 }, (_, i) => ({
          ref: `perf-${run}-${i}`,
          email: `${prefix}${run}-${i}@yopmail.com`,
          memberships: inDefault([BATCH_ROLE]),
        }));
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
      console.log(`H15 ${JSON.stringify(result)}`);
      expect(result.submitP95, "submit p95 ≤ 1 s").toBeLessThanOrEqual(1_000);
      expect(result.jobP95, "job p95 ≤ 10 s").toBeLessThanOrEqual(10_000);
      passed = true;
    } finally {
      await finish(passed, page, tenant, undefined, prefix);
    }
  });
});
