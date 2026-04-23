import { getRuntimeEnv } from "@/lib/runtime-env";
/**
 * Gets the appropriate API path based on whether we're using localhost or a remote server
 * @param servicePath - The service path (e.g., 'idp/v1', 'communication/v1')
 * @returns '/api' for localhost, otherwise returns the service path
 */
export const getApiPath = (servicePath: string): string => {
  const isLocalhost = getRuntimeEnv("BLOCKS_API_BASE_URL")?.includes("localhost");
  return isLocalhost ? "/api" : `/${servicePath}`;
};

/**
 * Constructs a full API URL
 * @param servicePath - The service path (e.g., 'idp/v1')
 * @param endpoint - The endpoint path (e.g., 'Authentication/Login')
 * @returns Full URL
 */
export const getApiUrl = (servicePath: string, endpoint: string): string => {
  const baseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL");
  const apiPath = getApiPath(servicePath);
  return `${baseUrl}${apiPath}/${endpoint}`;
};
