import { getRuntimeEnv } from "@/lib/runtime-env";

/**
 * Returns the global API path prefix. All app API routes use `/api`.
 */
export const getApiPath = (): string => {
  return "/api";
};

/**
 * Constructs a full API URL: base origin + `/api` + `/${endpoint}`.
 * @param endpoint - The path after `/api` (e.g. `Authentication/Login`, `.well-known/jwks.json`)
 */
export const getApiUrl = (endpoint: string): string => {
  const baseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL");
  const apiPath = getApiPath();
  return `${baseUrl}${apiPath}/${endpoint}`;
};
