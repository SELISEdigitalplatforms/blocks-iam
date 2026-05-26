import { useEffect, useMemo, useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { KeyRound, Loader, Lock, ShieldCheck } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/useAuthStore";
import { Signin } from "@blocks-idp/authentication/pages/login";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { OidcLoginForm } from "./oidc-login-form";
import { Logo } from "@/components/logo";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

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
  const effectiveClientId =  urlClientId || "";

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
  // Pure OIDC password/email flow — centered single-column layout
  if (isOidcPasswordFlow && (oidcContext.clientId || urlClientId) && (oidcContext.redirectUri || urlRedirectUri)) {
    return (
      <div className="fixed inset-0 z-50 flex flex-col bg-[hsl(var(--surface-app))]">
        {/* Background decorations */}
        <div className="pointer-events-none absolute inset-0 overflow-hidden">
          <div className="absolute -left-40 -top-40 h-96 w-96 rounded-full bg-primary/5 blur-3xl" />
          <div className="absolute -bottom-40 right-10 h-80 w-80 rounded-full bg-primary/5 blur-3xl" />
          <div className="absolute left-1/2 top-1/4 h-64 w-64 -translate-x-1/2 rounded-full bg-primary/3 blur-3xl" />
        </div>

        {/* Header — same as LoginSimplePage */}
        <header className="relative z-10 flex items-center px-6 py-5 xl:px-[154px]">
          <Logo width={120} height={52} />
          <div className="absolute right-6 top-5 xl:right-[154px]">
            <ModeToggle />
          </div>
        </header>

        {/* Main — centered */}
        <main className="relative z-10 min-h-0 flex-1 overflow-y-auto">
          <div className="flex min-h-full items-center justify-center px-6 py-8">
          <div className="w-full max-w-[420px]">

            {/* Card */}
            <div className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-md">

              {/* Card header — matches carousel card style */}
              <div className="relative overflow-hidden rounded-t-2xl blocks-gradient px-6 py-7">
                <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
                <div className="absolute -bottom-6 right-4 h-20 w-20 rounded-full bg-white/5" />
                <span className="relative inline-flex items-center rounded-full bg-white/15 px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-widest text-primary-foreground/80">
                  Secure Authentication
                </span>
                <div className="relative mt-3 flex items-center gap-3">
                  {/* <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-white/20 backdrop-blur-sm">
                    <ShieldCheck className="h-5 w-5 text-primary-foreground" />
                  </div> */}
                  <div>
                    <h1 className="text-lg font-bold leading-tight text-primary-foreground">Blocks Identity Provider</h1>
                    <p className="mt-0.5 text-xs text-primary-foreground/70">Sign in to continue</p>
                  </div>
                </div>
              </div>

              {/* Card body — form */}
              <div className="p-6">
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
            </div>

            {/* Trust row */}
            <div className="mt-5 flex items-center justify-center gap-4">
              <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
                <ShieldCheck className="h-3.5 w-3.5 text-primary/60" />
                MFA Ready
              </div>
              <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
              <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
                <KeyRound className="h-3.5 w-3.5 text-primary/60" />
                SSO Enabled
              </div>
              <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
              <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
                <Lock className="h-3.5 w-3.5 text-primary/60" />
                Encrypted
              </div>
            </div>

            {/* Copyright */}
            <p className="mt-4 text-center text-[11px] text-[hsl(var(--low-emphasis))]">
              © {new Date().getFullYear()} SELISE Digital Platforms. All rights reserved.
            </p>
          </div>
          </div>
        </main>
      </div>
    );
  }

  return <Signin ssoError={ssoError} mode="oidc" oidcContext={contextPayload} />;
};
