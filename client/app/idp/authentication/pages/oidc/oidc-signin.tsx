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

        const isLocalhost = getRuntimeEnv("BLOCKS_API_BASE_URL")?.includes("localhost");
        if (isLocalhost && response.access_token && response.refresh_token) {
          setTokens(response.access_token, response.refresh_token);
        }

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

  return <Signin ssoError={ssoError} mode="oidc" oidcContext={contextPayload} />;
};
