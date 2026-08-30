const PLACEHOLDER_PREFIX = "__BLOCKS_";

type RuntimeKey =
  | "BLOCKS_IAM_BASE_URL"
  | "BLOCKS_X_BLOCKS_KEY"
  | "BLOCKS_GOOGLE_SITE_KEY"
  | "BLOCKS_CONSTRUCT_URL"
  | "BLOCKS_GITHUB_SSO_CLIENT_ID"
  | "BLOCKS_OIDC_CLIENT_ID"
  | "BLOCKS_BASE_DOMAIN"
  | "BLOCKS_IAM_CALLBACK_URL"
  | "BLOCKS_IAM_CLIENT_ID"
  | "BLOCKS_LOCALIZATION_BASE_URL"
  | "BLOCKS_LOCALIZATION_CALLBACK_URL"
  | "BLOCKS_LOCALIZATION_CLIENT_ID"
  | "BLOCKS_AGENTS_BASE_URL"
  | "BLOCKS_AGENTS_CALLBACK_URL"
  | "BLOCKS_AGENTS_CLIENT_ID"
  | "BLOCKS_DATA_BASE_URL"
  | "BLOCKS_DATA_CALLBACK_URL"
  | "BLOCKS_DATA_CLIENT_ID"
  | "BLOCKS_OS_BASE_URL"
  | "BLOCKS_OS_CALLBACK_URL"
  | "BLOCKS_OS_CLIENT_ID"
  | "BLOCKS_UTILITIES_BASE_URL"
  | "BLOCKS_UTILITIES_CALLBACK_URL"
  | "BLOCKS_UTILITIES_CLIENT_ID"
  | "BLOCKS_LOGIC_BASE_URL"
  | "BLOCKS_LOGIC_CALLBACK_URL"
  | "BLOCKS_LOGIC_CLIENT_ID"
  | "BLOCKS_MONITOR_BASE_URL"
  | "BLOCKS_MONITOR_CALLBACK_URL"
  | "BLOCKS_MONITOR_CLIENT_ID"
  | "BLOCKS_RELEASE_BASE_URL"
  | "BLOCKS_RELEASE_CALLBACK_URL"
  | "BLOCKS_RELEASE_CLIENT_ID"
  | "BLOCKS_STUDIO_BASE_URL"
  | "BLOCKS_STUDIO_CALLBACK_URL";


const isPlaceholder = (value?: string) =>
  !!value && value.startsWith(PLACEHOLDER_PREFIX) && value.endsWith("__");

export const getRuntimeEnv = (key: RuntimeKey): string => {
  const windowValue = typeof window !== "undefined" ? (window.__BLOCKS_ENV__ as Partial<Record<RuntimeKey, string>>)?.[key] : undefined;
  if (windowValue && !isPlaceholder(windowValue)) {
    return windowValue;
  }

  return import.meta.env[key] || "";
};

/**
 * Base URL for calls to IAM's *own* API. The SPA is always served by the same
 * host that serves `/api/*` (the client is built into `server/Api/wwwroot` and
 * one Kestrel serves both), so the browser origin is authoritative.
 *
 * Prefer this over `getRuntimeEnv("BLOCKS_IAM_BASE_URL")` for self-calls:
 * that value is baked into wwwroot at container start, when the app cannot yet
 * know which host it will be served on, so it is wrong on any host other than
 * the canonical one — notably per-PR preview environments. Cross-service URLs
 * (BLOCKS_LOGIC_BASE_URL etc.) must still come from `getRuntimeEnv`.
 */
export const getSelfBaseUrl = (): string =>
  typeof window !== "undefined" ? window.location.origin : "";
