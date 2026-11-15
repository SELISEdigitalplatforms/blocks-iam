import { useEffect, useMemo, useState } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router";
import { Loader } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { Signin } from "@blocks-idp/authentication/pages/login";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { OidcLoginForm } from "./oidc-login-form";
import { OidcAuthShell, OidcFooter } from "./oidc-auth-shell";
import {
  OidcLoginPageSkeleton,
  readRememberedSidePanel,
  rememberSidePanel,
} from "./oidc-login-skeleton";
import { OIDC_LOGIN_PANEL } from "./oidc-panel-config";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { tStatic, useT } from "@seliseblocks/genesis-os/i18n";

export const OIDCSignin = () => {
  const { t } = useT("iam.oidc");
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { setAuthenticated, setTokens } = useAuthStore();
  const [isActivatingSocial, setIsActivatingSocial] = useState(false);
  const oidcContext = useOIDCContext();
  const tenantId = oidcContext.tenantId || searchParams.get("tenant_id") || undefined;
  const {
    data: oidcUiConfig,
    isLoading: isUiConfigLoading,
    isSuccess: isUiConfigLoaded,
  } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;
  const showAnimationPanel = template?.layout.showAnimationPanel;

  // Remembered so the next visit's skeleton already has this tenant's shape (panel or not).
  useEffect(() => {
    if (isUiConfigLoaded && showAnimationPanel !== undefined) {
      rememberSidePanel(tenantId, showAnimationPanel);
    }
  }, [isUiConfigLoaded, showAnimationPanel, tenantId]);

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
  const effectiveRedirectUri = oidcContext.redirectUri || urlRedirectUri || "";
  // Pure OIDC password/email flow: the sci-fi shell with the nodes panel. Also covers the
  // device-flow return trip, which has returnUrl instead of redirect_uri.
  const rendersLoginShell =
    isOidcPasswordFlow && !!effectiveClientId && !!(effectiveRedirectUri || effectiveReturnUrl);

  // The page is drawn from three answers: the tenant's template (theme, brand, side panel), the
  // application's login options (password, which social providers) and the tenant's sign-up
  // setting (the "create an account" link). All three are asked for here, side by side, and the
  // page waits for all of them, so it renders once, as configured, instead of drawing defaults
  // and correcting them. The form reads the same queries (same keys) from the cache.
  const { isLoading: isLoginOptionsLoading } = useGetLoginOptions(
    tenantId,
    rendersLoginShell,
    effectiveClientId,
  );
  const { isLoading: isSignUpSettingLoading } = useGetSignUpSetting(tenantId, {
    enabled: rendersLoginShell,
  });
  const isPageConfigLoading = isUiConfigLoading || isLoginOptionsLoading || isSignUpSettingLoading;

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

        // Not authenticated until the second factor clears -- the store is marked
        // after the MFA branch, not before it. The other two callers of this response
        // (signin-form, use-sso-activation) already order it this way.
        if (response?.mfa_required && response.mfa_id) {
          navigate(
            buildOIDCNavigationUrl(
              `/mfa-check?mfa_id=${encodeURIComponent(response.mfa_id)}&mfa_type=${response.mfa_type ?? 0}`,
            ),
          );
          return;
        }

        setAuthenticated();

        const redirectResult = response as typeof response & {
          redirect_url?: string;
          sso_user_redirect_url?: string;
        };
        const redirectUrl = redirectResult?.redirect_url || redirectResult?.sso_user_redirect_url;
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
        showErrorToast({ errors: tStatic("iam.oidc", "OIDC_SIGNIN.SOCIAL_SIGN_IN_FAILED") });
      } finally {
        setIsActivatingSocial(false);
      }
    };

    activateSocial();
  }, [code, navigate, oidcContext.clientId, oidcContext.userName, setAuthenticated, setTokens, state]);

  // `/oidc/login` with no application at all — no client, no return address, no tenant —
  // is reached from a bookmark or an IAM page whose "Sign in" link lost the flow. The
  // fallback card below then reads IAM's own login options, and IAM's own tenant allows
  // only authorization_code, so it can only render "this sign-in link is missing the
  // application". Send the user to `/login` instead: it starts IAM's own OIDC request and
  // comes back here with a client, so the real form renders.
  const hasApplication = !!(
    effectiveClientId ||
    oidcContext.redirectUri ||
    urlRedirectUri ||
    effectiveReturnUrl ||
    tenantId
  );
  if (isOidcPasswordFlow && !code && !hasApplication) {
    return <Navigate to="/login" replace />;
  }

  if (isActivatingSocial) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader className="h-12 w-12 animate-spin text-gray-500" />
      </div>
    );
  }

  if (rendersLoginShell && (isPageConfigLoading || !template)) {
    return <OidcLoginPageSkeleton showSidePanel={readRememberedSidePanel(tenantId) ?? true} />;
  }

  if (rendersLoginShell && template) {
    return (
      <OidcAuthShell
        panelConfig={OIDC_LOGIN_PANEL}
        showAnimationPanel={template.layout.showAnimationPanel}
        theme={template.theme}
        logoUrlLight={template.branding.logoUrlLight}
        logoUrlDark={template.branding.logoUrlDark}
        brandName={template.branding.brandName}
        heading={template.pages.login.heading}
        headingDimFirst={3}
        successTitle={t("OIDC_SIGNIN.ACCESS_GRANTED")}
        successSubtitle={t("OIDC_SIGNIN.REDIRECTING")}
        showCorners={false}
        footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
      >
        <OidcLoginForm
          clientId={effectiveClientId}
          redirectUri={effectiveRedirectUri}
          returnUrl={effectiveReturnUrl}
          scope={oidcContext.scope || searchParams.get("scope") || undefined}
          state={oidcContext.state || searchParams.get("state") || undefined}
          nonce={oidcContext.nonce || searchParams.get("nonce") || undefined}
          tenantId={tenantId}
          codeChallenge={searchParams.get("code_challenge") || undefined}
          codeChallengeMethod={searchParams.get("code_challenge_method") || "S256"}
          initialError={ssoError}
          // Not on a return trip that carries a provider code: that round trip is this page's
          // own social sign-in, and starting another one from it could loop.
          autoStartSingleProvider={!code}
        />
      </OidcAuthShell>
    );
  }

  return <Signin ssoError={ssoError} mode="oidc" oidcContext={contextPayload} />;
};
