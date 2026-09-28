import { AlertTriangle } from "lucide-react";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import type { IOidcUiTemplate } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { Button } from "@/components/ui-kits/button/button";

type Props = { template: IOidcUiTemplate };

/** Single refusal card for every invalid/expired/revoked/exhausted link (C1). */
export function JoinInvalid({ template }: Props) {
  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading="Link unavailable"
      headingDimFirst={0}
      headingAlign="center"
      successTitle="You're in"
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <div className="flex flex-col items-center gap-4 py-6 text-center">
        <AlertTriangle className="h-10 w-10" style={{ color: "var(--warn, #f59e0b)" }} />
        <p className="text-sm" style={{ color: "var(--fg)" }}>
          This link is no longer valid.
        </p>
        <Button asChild variant="outline">
          <LoginReturnLink preferOidcLogin>Back to login</LoginReturnLink>
        </Button>
      </div>
    </OidcAuthShell>
  );
}
