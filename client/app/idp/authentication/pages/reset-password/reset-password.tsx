import { AlertTriangle } from "lucide-react";
import { Link } from "react-router";
import { OidcAuthShell } from "../oidc/oidc-auth-shell";
import { RESET_PASSWORD_PANEL } from "../oidc/oidc-panel-config";
import { ResetPasswordForm } from "./reset-password-form";

type ResetPasswordProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

export const ResetPassword = ({ code, tenantId }: ResetPasswordProps) => {
  return (
    <OidcAuthShell
      panelConfig={RESET_PASSWORD_PANEL}
      heading="Set a new password"
      headingDimFirst={3}
      successTitle="Password Updated"
      successSubtitle="Your password has been reset successfully."
      showCorners={false}
      footerNote={
        <p className="text-xs" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
          © {new Date().getFullYear()} SELISE Digital Platforms. All rights reserved.
        </p>
      }
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
