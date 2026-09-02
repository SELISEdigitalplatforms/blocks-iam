import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { OIDCForgotPasswordForm } from "./oidc-forgot-password-form";
import {
  buildOidcThemeStyle,
  OidcBrand,
  OidcFooter,
  useOidcResolvedTheme,
} from "../oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import "../oidc/sci-fi-oidc.css";

export const OIDCForgotPassword = () => {
  const { data: oidcUiConfig } = useOidcUiConfig();
  const template = oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE;
  const resolvedTheme = useOidcResolvedTheme();

  return (
    <div
      className="oidc-scifi-root min-h-screen overflow-hidden relative bg-[var(--bg)]"
      data-theme={resolvedTheme}
      style={buildOidcThemeStyle(template.theme[resolvedTheme])}
    >
      <SciFiBackgroundOidc showCorners={false} />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div className="w-full max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--node-bg)] p-10 backdrop-blur-[16px]">
          <div className="flex items-center gap-3 mb-8">
            <OidcBrand
              logoUrl={template.branding.logoUrl}
              brandName={template.branding.brandName}
            />
          </div>
          <OIDCForgotPasswordForm />
        </div>
        <OidcFooter footerText={template.pages.shared.footerText} />
      </main>
    </div>
  );
};
