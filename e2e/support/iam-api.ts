import type { Page } from "@playwright/test";

const TENANT_CLAIM = "tenant_id";

/** Decode JWT payload without verifying (E2E only). */
function decodeJwtPayload(jwt: string): Record<string, unknown> | null {
  const parts = jwt.split(".");
  if (parts.length < 2) return null;
  try {
    const json = Buffer.from(parts[1], "base64url").toString("utf8");
    return JSON.parse(json) as Record<string, unknown>;
  } catch {
    return null;
  }
}

/**
 * Resolve the caller's tenant id from the host-named access-token cookie
 * (set after OIDC login on PR preview / shared IAM).
 */
export async function resolveTenantId(page: Page): Promise<string> {
  const base = new URL(page.url()).hostname;
  const cookies = await page.context().cookies();
  const access = cookies.find((c) => c.name === base);
  if (!access?.value) {
    throw new Error(`No access-token cookie named ${base}`);
  }
  const payload = decodeJwtPayload(access.value);
  const tenantId = payload?.[TENANT_CLAIM];
  if (typeof tenantId !== "string" || !tenantId) {
    throw new Error("tenant_id claim missing from access token");
  }
  return tenantId;
}

export type IamApiResult<T = unknown> = {
  status: number;
  json: T;
  raw: string;
};

/**
 * Authenticated IAM JSON API call via in-page fetch so session cookies are
 * included. Always sends X-Blocks-Key (required on PR preview hosts).
 */
export async function iamApi<T = unknown>(
  page: Page,
  method: string,
  path: string,
  body?: unknown,
  tenantId?: string,
): Promise<IamApiResult<T>> {
  const tenant = tenantId ?? (await resolveTenantId(page));
  const result = await page.evaluate(
    async ({ method, path, body, tenant }) => {
      const res = await fetch(path, {
        method,
        credentials: "include",
        headers: {
          "Content-Type": "application/json",
          Accept: "application/json",
          "X-Blocks-Key": tenant,
        },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const raw = await res.text();
      return { status: res.status, raw };
    },
    { method, path, body, tenant },
  );
  let json: T;
  try {
    json = result.raw ? (JSON.parse(result.raw) as T) : ("" as T);
  } catch {
    json = result.raw as unknown as T;
  }
  return { status: result.status, json, raw: result.raw };
}
