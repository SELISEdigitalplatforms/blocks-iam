import { Loader } from "lucide-react";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import type { IOidcUiTemplate } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";

type Props = { template?: IOidcUiTemplate };

/** Skeleton mirroring the OIDC card while /context is in flight. */
export function JoinLoading({ template }: Props) {
  return (
    <OidcAuthShell
      panelConfig={JOIN_PANEL}
      theme={template?.theme}
      logoUrlLight={template?.branding.logoUrlLight}
      logoUrlDark={template?.branding.logoUrlDark}
      brandName={template?.branding.brandName ?? "Blocks"}
      heading="Joining"
      headingDimFirst={0}
      headingAlign="left"
      showCorners={false}
      footerNote={
        <OidcFooter footerText={template?.pages.shared.footerText ?? ""} />
      }
    >
      <div className="flex flex-col items-center justify-center gap-4 py-10">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
        <p className="text-sm" style={{ color: "var(--muted)" }}>
          Checking your invite…
        </p>
      </div>
    </OidcAuthShell>
  );
}
