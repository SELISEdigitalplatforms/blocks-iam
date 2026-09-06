import { AlertTriangle, Loader } from "lucide-react";
import { Link } from "react-router";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { RESET_PASSWORD_PANEL } from "../oidc/oidc-panel-config";
import { ResetPasswordForm } from "./reset-password-form";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";

type ResetPasswordProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

export const ResetPassword = ({ code, tenantId }: ResetPasswordProps) => {
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-[var(--bg)]">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={RESET_PASSWORD_PANEL}
      theme={template.theme}
      logoUrl={template.branding.logoUrl}
      brandName={template.branding.brandName}
      heading={template.pages.resetPassword.heading}
      headingDimFirst={3}
      successTitle={template.pages.resetPassword.successTitle}
      successSubtitle={template.pages.resetPassword.successSubtitle}
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      {code ? (
        <ResetPasswordForm code={code} tenantId={tenantId} />
      ) : (
        <div className="flex flex-col items-center gap-4 py-4 text-center">
          <div
            className="w-12 h-12 rounded-full flex items-center justify-center"
            style={{ background: "rgba(234,179,8,.1)", border: "1px solid rgba(234,179,8,.25)" }}
          >
            <AlertTriangle size={22} style={{ color: "var(--warn)" }} />
          </div>
          <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
            The reset code is missing or invalid. Please request a new reset link.
          </p>
          <Link
            to="/forgot-password"
            className="oidc-sci-fi-btn"
            style={{ textDecoration: "none", display: "inline-block", textAlign: "center", padding: "10px 20px" }}
          >
            Request new reset link
          </Link>
        </div>
      )}
    </OidcAuthShell>
  );
};
