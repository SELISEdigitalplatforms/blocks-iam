import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useSearchParams } from "react-router-dom";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

interface OidcSocialSigninProps {
  clientId?: string;
  loginOption: any;
}

export const OidcSocialSignin = ({ clientId, loginOption }: OidcSocialSigninProps) => {
  const [searchParams] = useSearchParams();

  const redirectUri = searchParams.get("redirect_uri") || "";
  const scope = searchParams.get("scope") || "openid profile email";
  const state = searchParams.get("state") || "";
  const nonce = searchParams.get("nonce") || "";
  const codeChallenge = searchParams.get("code_challenge") || "";
  const codeChallengeMethod = searchParams.get("code_challenge_method") || "S256";
  const tenantId = searchParams.get("tenant_id") || "";

  const buildSocialLoginUrl = (provider: string) => {
    const baseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL") || "";
    const socialLoginUrl = new URL(`${baseUrl}/auth/social-login`);
    
    // Add OIDC context parameters
    const callbackUrl = buildOIDCNavigationUrl("/");
    socialLoginUrl.searchParams.set("provider", provider);
    socialLoginUrl.searchParams.set("callback", callbackUrl);
    socialLoginUrl.searchParams.set("clientId", clientId || "");
    
    // Preserve OIDC params
    if (redirectUri) socialLoginUrl.searchParams.set("redirect_uri", redirectUri);
    if (scope) socialLoginUrl.searchParams.set("scope", scope);
    if (state) socialLoginUrl.searchParams.set("state", state);
    if (nonce) socialLoginUrl.searchParams.set("nonce", nonce);
    if (codeChallenge) socialLoginUrl.searchParams.set("code_challenge", codeChallenge);
    if (codeChallengeMethod) socialLoginUrl.searchParams.set("code_challenge_method", codeChallengeMethod);
    if (tenantId) socialLoginUrl.searchParams.set("tenant_id", tenantId);

    return socialLoginUrl.toString();
  };

  const handleSocialLogin = (provider: string) => {
    const url = buildSocialLoginUrl(provider);
    window.location.href = url;
  };

  const socialProviders = loginOption?.socialProviders || [];

  if (!socialProviders || socialProviders.length === 0) {
    return null;
  }

  return (
    <div className="flex flex-col gap-3">
      {socialProviders.map((provider: any) => {
        const providerName = provider.name || provider.provider || "";
        const displayName = provider.displayName || providerName.charAt(0).toUpperCase() + providerName.slice(1);
        
        return (
          <Button
            key={providerName}
            type="button"
            variant="outline"
            className="w-full rounded"
            onClick={() => handleSocialLogin(providerName.toLowerCase())}
          >
            Sign in with {displayName}
          </Button>
        );
      })}
    </div>
  );
};
