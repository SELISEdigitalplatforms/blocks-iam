import { SOCIAL_AUTH_PROVIDERS_CONFIG } from "@blocks-idp/authentication/constants/sso-providers.constant";
import type { LoginOption } from "@blocks-idp/authentication/models/auth.model";
import type { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";

/**
 * The social providers a sign-in page shows as buttons, in display order: those the login
 * options list for this tenant and application that this page also supports. A provider the
 * page cannot render (not available here) is left out, so callers count what users see.
 */
export function getAvailableSsoProviders(
  loginOption: LoginOption | undefined,
): ISsoProviderConfigurationWithMeta[] {
  return Object.values(SOCIAL_AUTH_PROVIDERS_CONFIG).flatMap((config) => {
    const sso = loginOption?.ssoInfo?.find(
      (s) => s.provider === config.provider && config.isAvailable,
    );
    if (!sso) return [];

    return [
      {
        ...config,
        provider: sso.provider as typeof config.provider,
        clientId: sso.clientId as string,
      },
    ];
  });
}
