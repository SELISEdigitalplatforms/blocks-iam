import { useEffect, useRef, useState } from "react";
import { useParams } from "react-router";
import { Loader } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  useRedeemSignupLink,
  useSignupLinkContext,
} from "@blocks-idp/authentication/hooks/use-signup-link";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import { JoinInvalid } from "./join-invalid";
import { JoinLoading } from "./join-loading";

/**
 * Reads the signup code from location.hash exactly once, clears the hash via
 * replaceState before first paint completes (H5 / A1), then drives
 * loading → ready/auto-redeem → invalid.
 */
export function JoinPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const codeRef = useRef<string | null>(null);
  const [codeReady, setCodeReady] = useState(false);
  const redeemedRef = useRef(false);

  // Capture + clear hash synchronously on first mount (before paint where possible).
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
  const template = oidcUiConfig?.template;

  const contextQuery = useSignupLinkContext(codeReady ? code : null, tenantId);
  const redeemMutation = useRedeemSignupLink(tenantId);

  useEffect(() => {
    if (!contextQuery.data || !contextQuery.data.valid) return;
    if (contextQuery.data.credentialMode !== "Passwordless") return;
    if (!code || redeemedRef.current || redeemMutation.isPending || redeemMutation.isSuccess) {
      return;
    }
    redeemedRef.current = true;
    redeemMutation.mutate(code, {
      onSuccess: (res) => {
        if (res?.authorizeUrl) {
          window.location.assign(res.authorizeUrl);
        }
      },
      onError: () => {
        showErrorToast({ errors: "Something went wrong. Please try again." });
        redeemedRef.current = false;
      },
    });
  }, [contextQuery.data, code, redeemMutation]);

  if (!codeReady || contextQuery.isLoading || contextQuery.isFetching) {
    return <JoinLoading template={template} />;
  }

  if (!code || contextQuery.isError || !contextQuery.data?.valid) {
    return <JoinInvalid template={template} />;
  }

  // PasswordRequired is Phase 4 — show the ready card without auto-redeem.
  if (contextQuery.data.credentialMode === "PasswordRequired") {
    return (
      <OidcAuthShell
        panelConfig={JOIN_PANEL}
        theme={template?.theme}
        logoUrlLight={template?.branding.logoUrlLight}
        logoUrlDark={template?.branding.logoUrlDark}
        brandName={template?.branding.brandName ?? "Blocks"}
        heading={`Welcome, ${contextQuery.data.firstName}`}
        headingDimFirst={0}
        headingAlign="left"
        showCorners={false}
        footerNote={<OidcFooter footerText={template?.pages.shared.footerText ?? ""} />}
      >
        <div className="flex flex-col gap-3 py-4">
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            Continue into {contextQuery.data.applicationName} as{" "}
            {contextQuery.data.maskedEmail}. Password setup is required for this invite.
          </p>
        </div>
      </OidcAuthShell>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      theme={template?.theme}
      logoUrlLight={template?.branding.logoUrlLight}
      logoUrlDark={template?.branding.logoUrlDark}
      brandName={template?.branding.brandName ?? "Blocks"}
      heading={`Welcome, ${contextQuery.data.firstName}`}
      headingDimFirst={0}
      headingAlign="left"
      showCorners={false}
      footerNote={<OidcFooter footerText={template?.pages.shared.footerText ?? ""} />}
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
