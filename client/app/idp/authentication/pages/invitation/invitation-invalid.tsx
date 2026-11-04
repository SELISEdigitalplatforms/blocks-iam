import { AlertTriangle } from "lucide-react";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import type { IOidcUiTemplate } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { Button } from "@/components/ui-kits/button/button";
import { useT } from "@seliseblocks/genesis-os/i18n";

type Props = Readonly<{
  template: IOidcUiTemplate;
  /** Overrides the default refusal text, e.g. for a timed-out password step. */
  message?: string;
}>;

/** Single refusal card for every invalid/expired/revoked/exhausted link (C1). */
export function InvitationInvalid({ template, message }: Props) {
  const { t } = useT("iam.invitation");
  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      showAnimationPanel={template.layout.showAnimationPanel}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={t("INVITATION_INVALID.HEADING")}
      headingDimFirst={0}
      headingAlign="center"
      successTitle={t("INVITATION_INVALID.YOU_RE_IN")}
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <div className="flex flex-col items-center gap-4 py-6 text-center">
        <AlertTriangle className="h-10 w-10" style={{ color: "var(--warn, #f59e0b)" }} />
        <p className="text-sm" style={{ color: "var(--fg)" }}>
          {message ?? t("INVITATION_INVALID.LINK_INVALID")}
        </p>
        <Button asChild variant="outline">
          <LoginReturnLink preferOidcLogin>{t("INVITATION_INVALID.BACK_TO_LOGIN")}</LoginReturnLink>
        </Button>
      </div>
    </OidcAuthShell>
  );
}
