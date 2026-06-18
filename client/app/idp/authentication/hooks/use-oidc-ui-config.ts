import { useQuery } from "@tanstack/react-query";
import { serviceInstances } from "@/lib/http-client";

export interface IOidcUiCaptchaConfig {
  key: string;
  provider: string;
  generator: string;
}

export interface IOidcUiConfig {
  captcha: IOidcUiCaptchaConfig | null;
}

const OIDC_UI_CONFIG_ENDPOINT = "/idp/oidc-ui-config";

export const useOidcUiConfig = () => {
  return useQuery<IOidcUiConfig>({
    queryKey: ["oidc-ui-config"],
    queryFn: () =>
      serviceInstances.idpService
        .get(OIDC_UI_CONFIG_ENDPOINT, undefined, { absoluteUrl: true })
        .then((res: unknown) => (res as { data: IOidcUiConfig }).data),
  });
};