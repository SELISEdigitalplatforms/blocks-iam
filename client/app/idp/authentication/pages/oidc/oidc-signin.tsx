import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Loader } from "lucide-react";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/useAuthStore";
import { Signin } from "@blocks-idp/authentication/pages/login";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { OidcLoginForm } from "./oidc-login-form";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";

export const OIDCSignin = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { setAuthenticated, setTokens } = useAuthStore();
  const [isActivatingSocial, setIsActivatingSocial] = useState(false);
  const oidcContext = useOIDCContext();

  const code = searchParams.get("code") || "";
  const state = searchParams.get("state") || "";
  const ssoError = searchParams.get("error_description") || searchParams.get("error") || undefined;

  const contextPayload = useMemo(
    () => ({
      clientId: oidcContext.clientId,
      scope: oidcContext.scope,
      state: oidcContext.state,
      nonce: oidcContext.nonce,
      redirectUri: oidcContext.redirectUri,
      themeColor: oidcContext.themeColor,
    }),
    [oidcContext.clientId, oidcContext.nonce, oidcContext.redirectUri, oidcContext.scope, oidcContext.state, oidcContext.themeColor],
  );

  const isOidcPasswordFlow = window.location.pathname.includes("/oidc/login");
  const urlClientId = searchParams.get("client_id");
  const urlRedirectUri = searchParams.get("redirect_uri");
  const effectiveClientId = oidcContext.clientId || urlClientId || "";

  useEffect(() => {
    if (!code || !state) {
      return;
    }

    const activateSocial = async () => {
      try {
        setIsActivatingSocial(true);
        const response = await oauthService.signinBySSO({
          code,
          state,
          clientId: oidcContext.clientId,
        });

        // Root tenant FE uses only HttpOnly cookies; no token handling from response needed

        setAuthenticated();

        if ((response as any)?.enable_mfa) {
          const mfaId = (response as any)?.mfaId;
          const mfaType = (response as any)?.mfaType;
          navigate(buildOIDCNavigationUrl(`/mfa-check?mfa_id=${mfaId}&mfa_type=${mfaType}`));
          return;
        }

        const redirectUrl = (response as any)?.redirect_url || (response as any)?.sso_user_redirect_url;
        if (redirectUrl) {
          window.location.href = redirectUrl;
          return;
        }

        const params = getCurrentOIDCParams();
        if (oidcContext.userName) {
          params.set("userName", oidcContext.userName);
        }
        navigate(`/oidc/permission?${params.toString()}`);
      } catch {
        showErrorToast({ errors: "Social sign in failed. Please try again." });
      } finally {
        setIsActivatingSocial(false);
      }
    };

    activateSocial();
  }, [code, navigate, oidcContext.clientId, oidcContext.userName, setAuthenticated, setTokens, state]);

  if (isActivatingSocial) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader className="h-12 w-12 animate-spin text-gray-500" />
      </div>
    );
  }

  // Pure OIDC password/email flow - show if on /oidc/login with client_id param
  if (isOidcPasswordFlow && (oidcContext.clientId || urlClientId) && (oidcContext.redirectUri || urlRedirectUri)) {
    return (
      <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
        <CardHeader className="text-center">
          <CardTitle className="text-3xl">Blocks Cloud</CardTitle>
          <CardDescription className="text-xl text-foreground">Log in</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-1 flex-col justify-between">
          <div className="flex flex-1 flex-col justify-center">
            <OidcLoginForm
              clientId={effectiveClientId}
              redirectUri={oidcContext.redirectUri || urlRedirectUri || ""}
              scope={oidcContext.scope || searchParams.get("scope") || undefined}
              state={oidcContext.state || searchParams.get("state") || undefined}
              nonce={oidcContext.nonce || searchParams.get("nonce") || undefined}
              tenantId={oidcContext.tenantId || searchParams.get("tenant_id") || undefined}
              codeChallenge={searchParams.get("code_challenge") || undefined}
              codeChallengeMethod={searchParams.get("code_challenge_method") || "S256"}
            />
          </div>
        </CardContent>
      </Card>
    );
  }

  return <Signin ssoError={ssoError} mode="oidc" oidcContext={contextPayload} />;
};
