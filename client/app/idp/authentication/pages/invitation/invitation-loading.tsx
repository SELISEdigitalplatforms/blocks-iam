import { Loader } from "lucide-react";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import { useT } from "@seliseblocks/genesis-os/i18n";
import type { IOidcUiTemplate } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";

type Props = Readonly<{ template: IOidcUiTemplate }>;

/** Skeleton mirroring the OIDC card while /context is in flight. */
export function InvitationLoading({ template }: Props) {
  const { t } = useT("iam.invitation");
  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      showAnimationPanel={template.layout.showAnimationPanel}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={t("INVITATION_LOADING.HEADING")}
      headingDimFirst={0}
      headingAlign="left"
      successTitle={t("INVITATION_LOADING.YOU_RE_IN")}
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <div className="flex flex-col items-center justify-center gap-4 py-10">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
        <p className="text-sm" style={{ color: "var(--muted)" }}>
          {t("INVITATION_LOADING.CHECKING")}
        </p>
      </div>
    </OidcAuthShell>
  );
}
