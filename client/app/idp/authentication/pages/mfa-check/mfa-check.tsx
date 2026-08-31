import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { parseAsInteger, useQueryStates } from "nuqs";
import { MfaCheckFrom } from "./mfa-check-form";
import {
  buildOidcThemeStyle,
  OidcBrand,
  OidcFooter,
} from "../oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import "../oidc/sci-fi-oidc.css";

export const MfaCheck = () => {
  const { data: oidcUiConfig } = useOidcUiConfig();
  const template = oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE;
  const [{ mfa_type }] = useQueryStates({
    mfa_type: parseAsInteger.withDefault(0),
  });
  const mfa_type_message =
    mfa_type == 1
      ? "Open your authenticator app and enter the verification code."
      : "Check your email for the verification code and enter it here to continue.";

  return (
    <div
      className="oidc-scifi-root min-h-screen overflow-hidden relative bg-[var(--bg)]"
      style={buildOidcThemeStyle(template.theme)}>
      <SciFiBackgroundOidc showCorners={false} />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div className="w-full max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--node-bg)] p-10 backdrop-blur-[16px]">
          <div className="mb-8 flex items-start justify-between gap-4">
            <OidcBrand
              logoUrl={template.branding.logoUrl}
              brandName={template.branding.brandName}
            />
          </div>

          <div className="mb-6">
            <h2 className="text-xl font-semibold mb-2 font-sans text-[var(--fg)]">
              {template.pages.mfa.heading}
            </h2>
            <p className="text-sm font-sans text-[var(--muted)]">
              {mfa_type_message}
            </p>
          </div>

          <MfaCheckFrom />
        </div>
        <OidcFooter footerText={template.pages.shared.footerText} />
      </main>
    </div>
  );
};
