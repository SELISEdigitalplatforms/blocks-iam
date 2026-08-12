import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { Loader } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { Signin } from "@blocks-idp/authentication/pages/login";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { OidcLoginForm } from "./oidc-login-form";
import { OidcAuthShell } from "./oidc-auth-shell";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";

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
  // The client id arrives as `client_id` from the authorize endpoint but as
  // `clientId` from in-app navigation (buildOIDCNavigationUrl). Reading only the
  // snake_case form drops the OIDC flow and falls back to the plain login card,
  // so accept both — same tolerance extractOIDCParams already applies.
  const urlClientId = searchParams.get("client_id") || searchParams.get("clientId");
  const urlRedirectUri = searchParams.get("redirect_uri");
  // Device flow (RFC 8628) logins have no redirect_uri — the IAM's device verification
  // page passes `returnUrl` instead, so the OIDC-aware login form still renders (and its
  // post-login redirect uses returnUrl in place of the usual redirect_uri).
  const urlReturnUrl = searchParams.get("returnUrl");
  const effectiveReturnUrl = oidcContext.returnUrl || urlReturnUrl || undefined;
  const effectiveClientId = urlClientId || oidcContext.clientId || "";

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
  // Pure OIDC password/email flow — sci-fi shell with nodes panel. Also covers the
  // device-flow return trip, which has returnUrl instead of redirect_uri.
  if (
    isOidcPasswordFlow &&
    effectiveClientId &&
    (oidcContext.redirectUri || urlRedirectUri || effectiveReturnUrl)
  ) {
    return (
      <OidcAuthShell
        panelConfig={OIDC_LOGIN_PANEL}
        heading="Sign in to continue to your application"
        headingDimFirst={3}
        showCorners={false}
        footerNote={
          <p className="text-xs" style={{ fontFamily: "'Rajdhani', sans-serif", color: "var(--muted)" }}>
            © {new Date().getFullYear()} SELISE Digital Platforms. All rights reserved.
          </p>
        }
      >
        <OidcLoginForm
          clientId={effectiveClientId}
          redirectUri={oidcContext.redirectUri || urlRedirectUri || ""}
          returnUrl={effectiveReturnUrl}
          scope={oidcContext.scope || searchParams.get("scope") || undefined}
          state={oidcContext.state || searchParams.get("state") || undefined}
          nonce={oidcContext.nonce || searchParams.get("nonce") || undefined}
          tenantId={oidcContext.tenantId || searchParams.get("tenant_id") || undefined}
          codeChallenge={searchParams.get("code_challenge") || undefined}
          codeChallengeMethod={searchParams.get("code_challenge_method") || "S256"}
        />
      </OidcAuthShell>
    );
  }

  return <Signin ssoError={ssoError} mode="oidc" oidcContext={contextPayload} />;
};
