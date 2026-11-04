import { Loader } from "lucide-react";
import { useState } from "react";
import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { MfaCheckFrom } from "./mfa-check-form";
import {
  buildOidcThemeStyle,
  OidcBrand,
  OidcFooter,
  useOidcResolvedTheme,
} from "../oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useT } from "@seliseblocks/genesis-os/i18n";
import "../oidc/sci-fi-oidc.css";

export const MfaCheck = () => {
  const { t } = useT("iam.mfa-check");
  const { data: oidcUiConfig } = useOidcUiConfig();
  const template = oidcUiConfig?.template;
  const resolvedTheme = useOidcResolvedTheme();
  const [{ mfa_type: mfaType, mfa_id: mfaId }] = useQueryStates({
    mfa_type: parseAsInteger.withDefault(0),
    mfa_id: parseAsString.withDefault(""),
  });
  // Set by the form once the server says the sign-in is gone; a URL without a challenge never had one.
  const [expired, setExpired] = useState(false);
  const sessionEnded = expired || !mfaId;
  const mfaTypeMessage =
    mfaType == 1
      ? t("MFA_CHECK.MESSAGE_AUTHENTICATOR")
      : t("MFA_CHECK.MESSAGE_EMAIL");

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  return (
    <div
      className="oidc-scifi-root min-h-screen overflow-hidden relative bg-(--bg)"
      data-theme={resolvedTheme}
      style={buildOidcThemeStyle(template.theme[resolvedTheme])}>
      <SciFiBackgroundOidc showCorners={false} />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div className="w-full max-w-lg rounded-2xl border border-(--border) bg-(--node-bg) p-10 backdrop-blur-lg">
          <div className="mb-8 flex items-start justify-between gap-4">
            <OidcBrand
              logoUrl={resolvedTheme === "dark" ? template.branding.logoUrlDark : template.branding.logoUrlLight}
              brandName={template.branding.brandName}
            />
          </div>

          <div className="mb-6">
            <h2 className="text-xl font-semibold mb-2 font-sans text-(--fg)">
              {sessionEnded ? t("MFA_CHECK.SESSION_EXPIRED_TITLE") : template.pages.mfa.heading}
            </h2>
            {!sessionEnded && (
              <p className="text-sm font-sans text-(--muted)">
                {mfaTypeMessage}
              </p>
            )}
          </div>

          <MfaCheckFrom onSessionExpired={() => setExpired(true)} />
        </div>
        <OidcFooter footerText={template.pages.shared.footerText} />
      </main>
    </div>
  );
};
