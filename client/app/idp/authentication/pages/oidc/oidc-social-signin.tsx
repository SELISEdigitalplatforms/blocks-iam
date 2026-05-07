import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useSearchParams } from "react-router-dom";

interface OidcSocialSigninProps {
  loginOption: any;
  clientId?: string;
}

export const OidcSocialSignin = ({ loginOption, clientId }: OidcSocialSigninProps) => {
  const [searchParams] = useSearchParams();
  // Prefer oidc_state (server-created session key) over the PKCE state param
  const oidcState = searchParams.get("oidc_state") || "";

  const getAuthorizationUrl = async (provider: string, prebuiltAuthorizationUrl?: string) => {
    if (prebuiltAuthorizationUrl) {
      return prebuiltAuthorizationUrl;
    }

    const tenantKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
    const fetchHeaders: Record<string, string> = {};
    if (tenantKey) fetchHeaders["X-Blocks-Key"] = tenantKey;

    const oidcAuthorizeEndpoint = `/api/auth/oidc/social/authorize?provider=${encodeURIComponent(provider)}&oidcState=${encodeURIComponent(oidcState)}`;
    const socialAuthorizeEndpoint = `/api/auth/social/authorize?provider=${encodeURIComponent(provider)}`;

    const endpoint = oidcState ? oidcAuthorizeEndpoint : socialAuthorizeEndpoint;

    const response = await fetch(endpoint, {
      method: "GET",
      headers: fetchHeaders,
      credentials: "include",
    });

    const body = await response.json().catch(() => ({}));
    if (!response.ok) {
      throw new Error(
        body?.error_description ||
        body?.error ||
        body?.Errors?.Message ||
        body?.errors?.Message ||
        "Failed to initialize social login",
      );
    }

    return body?.authorizationUrl as string | undefined;
  };

  // API returns social providers with pre-built authorization URLs
  let socialProviders = loginOption?.ssoInfo || [];

  // Default social providers if loginOption is not available
  const DEFAULT_PROVIDERS = [
    { provider: "google", displayName: "Google" },
    { provider: "microsoft", displayName: "Microsoft" },
    { provider: "github", displayName: "GitHub" },
    { provider: "linkedin", displayName: "LinkedIn" },
  ];

  // Use default providers if none are configured
  if (!socialProviders || socialProviders.length === 0) {
    socialProviders = DEFAULT_PROVIDERS;
  }

  if (!socialProviders || socialProviders.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-3">
      {socialProviders.map((provider: any) => {
        const displayName = provider.displayName || (provider.provider?.charAt(0).toUpperCase() + provider.provider?.slice(1));
        const authorizationUrl = provider.authorizationUrl;

        return (
          <Button
            key={provider.provider}
            type="button"
            variant="outline"
            className="w-full rounded"
            onClick={async () => {
              try {
                const url = await getAuthorizationUrl(provider.provider, authorizationUrl);
                if (!url) {
                  throw new Error("Authorization URL was not returned by the server");
                }
                window.location.href = url;
              } catch (error) {
                showErrorToast({
                  errors: error instanceof Error ? error.message : "Social authorization failed",
                });
              }
            }}
          >
            Sign in with {displayName}
          </Button>
        );
      })}
    </div>
  );
};
