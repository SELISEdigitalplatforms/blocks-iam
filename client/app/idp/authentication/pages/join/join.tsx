import { useEffect, useRef, useState } from "react";
import { useParams } from "react-router";
import { Loader } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  useCompleteSignupLinkMfa,
  useRedeemSignupLink,
  useSignupLinkContext,
} from "@blocks-idp/authentication/hooks/use-signup-link";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import { JoinInvalid } from "./join-invalid";
import { JoinLoading } from "./join-loading";
import type { RedeemSignupLinkResponse } from "@blocks-idp/authentication/services/signup-link.service";

/**
 * Reads the signup code from location.hash exactly once, clears the hash via
 * replaceState before first paint completes (H5 / A1), then drives
 * loading → ready/auto-redeem → invalid. Phase 4 also handles PasswordRequired,
 * loginUrl redirects, and MFA-before-cookie.
 */
export function JoinPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const codeRef = useRef<string | null>(null);
  const [codeReady, setCodeReady] = useState(false);
  const redeemedRef = useRef(false);
  const [mfaId, setMfaId] = useState<string | null>(null);
  const [mfaCode, setMfaCode] = useState("");
  const [activationKey, setActivationKey] = useState<string | null>(null);
  const [userMfa, setUserMfa] = useState<string | null>(null);

  if (codeRef.current === null && typeof window !== "undefined" && !codeReady) {
    const hash = window.location.hash.startsWith("#")
      ? window.location.hash.slice(1)
      : window.location.hash;
    const params = new URLSearchParams(hash);
    const raw = params.get("link") ?? (hash.startsWith("link=") ? hash.slice(5) : "");
    codeRef.current = raw || null;
    if (window.location.hash) {
      const url = `${window.location.pathname}${window.location.search}`;
      window.history.replaceState(null, "", url);
    }
  }

  useEffect(() => {
    setCodeReady(true);
  }, []);

  const code = codeRef.current;
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template ?? null;

  const contextQuery = useSignupLinkContext(codeReady ? code : null, tenantId);
  const redeemMutation = useRedeemSignupLink(tenantId);
  const mfaMutation = useCompleteSignupLinkMfa(tenantId);

  const applyRedeemResult = (res: RedeemSignupLinkResponse | undefined) => {
    if (!res) return;
    if (res.authorizeUrl) {
      window.location.assign(res.authorizeUrl);
      return;
    }
    if (res.loginUrl) {
      window.location.assign(res.loginUrl);
      return;
    }
    if (res.mfaId) {
      setMfaId(res.mfaId);
      setUserMfa(res.userMfa ?? null);
      return;
    }
    if (res.activationKey) {
      setActivationKey(res.activationKey);
      return;
    }
  };

  useEffect(() => {
    if (!contextQuery.data || !contextQuery.data.valid) return;
    if (!code || redeemedRef.current || redeemMutation.isPending || redeemMutation.isSuccess) {
      return;
    }
    // Auto-redeem both modes; PasswordRequired returns activationKey instead of authorizeUrl.
    redeemedRef.current = true;
    redeemMutation.mutate(code, {
      onSuccess: applyRedeemResult,
      onError: () => {
        showErrorToast({ errors: "Something went wrong. Please try again." });
        redeemedRef.current = false;
      },
    });
  }, [contextQuery.data, code, redeemMutation]);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-[var(--bg)]">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  if (!codeReady || contextQuery.isLoading || contextQuery.isFetching) {
    return <JoinLoading template={template} />;
  }

  if (!code || contextQuery.isError || !contextQuery.data?.valid) {
    return <JoinInvalid template={template} />;
  }

  if (mfaId) {
    return (
      <OidcAuthShell
        panelConfig={JOIN_PANEL}
        theme={template.theme}
        logoUrlLight={template.branding.logoUrlLight}
        logoUrlDark={template.branding.logoUrlDark}
        brandName={template.branding.brandName}
        heading="Verify it's you"
        headingDimFirst={0}
        headingAlign="left"
        successTitle="You're in"
        successSubtitle=""
        showCorners={false}
        footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
      >
        <form
          className="flex flex-col gap-3 py-4"
          onSubmit={(e) => {
            e.preventDefault();
            mfaMutation.mutate(
              { mfaId, mfaCode },
              {
                onSuccess: applyRedeemResult,
                onError: () =>
                  showErrorToast({ errors: "Invalid verification code. Please try again." }),
              },
            );
          }}
        >
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            Enter the {userMfa ?? "MFA"} code to finish joining{" "}
            {contextQuery.data.applicationName}.
          </p>
          <input
            className="rounded border px-3 py-2 text-sm"
            value={mfaCode}
            onChange={(e) => setMfaCode(e.target.value)}
            autoComplete="one-time-code"
            aria-label="MFA code"
          />
          <button
            type="submit"
            className="rounded px-3 py-2 text-sm font-medium"
            style={{ background: "var(--accent)", color: "var(--bg)" }}
            disabled={mfaMutation.isPending || !mfaCode}
          >
            Continue
          </button>
        </form>
      </OidcAuthShell>
    );
  }

  if (activationKey || contextQuery.data.credentialMode === "PasswordRequired") {
    return (
      <OidcAuthShell
        panelConfig={JOIN_PANEL}
        theme={template.theme}
        logoUrlLight={template.branding.logoUrlLight}
        logoUrlDark={template.branding.logoUrlDark}
        brandName={template.branding.brandName}
        heading={`Welcome, ${contextQuery.data.firstName}`}
        headingDimFirst={0}
        headingAlign="left"
        successTitle="You're in"
        successSubtitle=""
        showCorners={false}
        footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
      >
        <div className="flex flex-col gap-3 py-4">
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            Continue into {contextQuery.data.applicationName} as{" "}
            {contextQuery.data.maskedEmail}. Password setup is required for this invite.
          </p>
          {activationKey ? (
            <p className="text-xs break-all" style={{ color: "var(--muted)" }} data-testid="activation-key">
              Activation ready. Complete password setup with your application.
            </p>
          ) : (
            <div className="flex flex-col items-center gap-3 py-4">
              <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
              <p className="text-sm" style={{ color: "var(--muted)" }}>
                Preparing your invite…
              </p>
            </div>
          )}
        </div>
      </OidcAuthShell>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={`Welcome, ${contextQuery.data.firstName}`}
      headingDimFirst={0}
      headingAlign="left"
      successTitle="You're in"
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <div className="flex flex-col items-center gap-3 py-8">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
        <p className="text-sm" style={{ color: "var(--muted)" }}>
          Signing you into {contextQuery.data.applicationName}…
        </p>
      </div>
    </OidcAuthShell>
  );
}

export default JoinPage;
