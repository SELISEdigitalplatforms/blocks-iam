import { useQuery } from "@tanstack/react-query";
import { serviceInstances } from "@/lib/http-client";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { extractOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

export interface IOidcUiCaptchaConfig {
  key: string;
  provider: string;
  generator: string;
}

export interface IOidcUiConfig {
  captcha: IOidcUiCaptchaConfig | null;
}

const OIDC_UI_CONFIG_ENDPOINT = "/api/idp/oidc-ui-config";

const TENANT_PATH_PATTERNS: RegExp[] = [
  /^\/oidc\/recover\/([^/?#]+)/,
  /^\/oidc\/activate\/([^/?#]+)/,
];

const resolveTenantIdFromPath = (): string | undefined => {
  if (typeof window === "undefined") return undefined;
  const path = window.location.pathname;
  for (const pattern of TENANT_PATH_PATTERNS) {
    const match = path.match(pattern);
    if (match?.[1]) return decodeURIComponent(match[1]);
  }
  return undefined;
};

const resolveTenantId = (override?: string): string => {
  if (override) return override;
  if (typeof window !== "undefined") {
    const fromUrl = extractOIDCParams().tenantId;
    if (fromUrl) return fromUrl;
    const fromPath = resolveTenantIdFromPath();
    if (fromPath) return fromPath;
  }
  return getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";
};

export const useOidcUiConfig = (tenantIdOverride?: string) => {
  const tenantId = resolveTenantId(tenantIdOverride);
  const url = tenantId
    ? `${OIDC_UI_CONFIG_ENDPOINT}?tenantId=${encodeURIComponent(tenantId)}`
    : OIDC_UI_CONFIG_ENDPOINT;

  const headers: Record<string, string> = tenantId
    ? { "X-Blocks-Key": tenantId }
    : {};

  const query = useQuery<IOidcUiConfig>({
    queryKey: ["oidc-ui-config", tenantId],
    queryFn: () =>
      serviceInstances.idpService.get(url, headers, {
        absoluteUrl: true,
        skipBlocksKey: true,
      }) as Promise<IOidcUiConfig>,
  });

  const captchaEnabled = query.data?.captcha != null;

  return {
    ...query,
    captchaEnabled,
  };
};