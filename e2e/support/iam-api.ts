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

/**
 * Read one claim from the host-named access-token cookie. Returns undefined
 * when the cookie or claim is absent. Never returns or logs the token itself.
 */
export async function readAccessTokenClaim(page: Page, claim: string): Promise<unknown> {
  const base = new URL(page.url()).hostname;
  const cookies = await page.context().cookies();
  const access = cookies.find((c) => c.name === base);
  if (!access?.value) return undefined;
  return decodeJwtPayload(access.value)?.[claim];
}

/** Error fields of an OIDC/IAM response. Token values are deliberately not exposed. */
export type TokenCallOutcome = {
  status: number;
  error?: string;
  errorDescription?: string;
};

/**
 * Refresh the browser's session through `POST /api/oidc/token`
 * (`grant_type=refresh_token`), exactly as a Blocks app does: form body with
 * `client_id`, the refresh token travelling in the HttpOnly `rt_` cookie, and
 * `credentials: "include"` so the rotated cookies land in the browser jar.
 * Runs in-page on purpose: a refresh made outside the browser rotates the
 * token while the browser keeps the old cookie.
 */
export async function oidcRefresh(
  page: Page,
  clientId: string,
  tenantId?: string,
): Promise<TokenCallOutcome> {
  const tenant = tenantId ?? (await resolveTenantId(page));
  return page.evaluate(
    async ({ clientId, tenant }) => {
      const res = await fetch("/api/oidc/token", {
        method: "POST",
        credentials: "include",
        headers: {
          "Content-Type": "application/x-www-form-urlencoded",
          Accept: "application/json",
          "X-Blocks-Key": tenant,
        },
        body: new URLSearchParams({ grant_type: "refresh_token", client_id: clientId }).toString(),
      });
      let error: string | undefined;
      let errorDescription: string | undefined;
      try {
        const json = (await res.json()) as { error?: string; error_description?: string };
        error = json.error;
        errorDescription = json.error_description;
      } catch {
        // Non-JSON body: the status alone is reported.
      }
      return { status: res.status, error, errorDescription };
    },
    { clientId, tenant },
  );
}
