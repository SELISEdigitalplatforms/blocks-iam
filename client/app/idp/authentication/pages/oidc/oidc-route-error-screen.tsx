import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import { AlertCircle } from "lucide-react";
import { useEffect, useRef } from "react";
import { useLocation, useParams, useRouteError, useSearchParams } from "react-router";
import { useT } from "@seliseblocks/genesis-os/i18n";
import { PanelFailure, TONE_CLASSES } from "./error-screen";
import { OidcAuthShell, OidcFooter } from "./oidc-auth-shell";
import { OIDC_ERROR_PANEL } from "./oidc-panel-config";
import { reportOidcRouteError } from "./oidc-route-error-report";

/**
 * Error element of the OIDC routes. A render error there used to unmount the whole tree and
 * leave a blank page with the user's emailed code unused (blocks-iam#682). This screen keeps the
 * error screen's look, reports the error once, and offers a reload of the same URL (code,
 * clientId, redirect_uri and lang included) or the way back to login.
 *
 * It sits outside OidcLayout, so it reads the tenant from the route itself.
 */
export const OidcRouteErrorScreen = () => {
  const { t } = useT("iam.oidc");
  const error = useRouteError();
  const { pathname } = useLocation();
  const { tenantId } = useParams();
  const [searchParams] = useSearchParams();
  // Falls back to the default template when the config call fails, so this screen always renders.
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId || searchParams.get("tenant_id") || undefined);
  const template = oidcUiConfig.template ?? DEFAULT_OIDC_UI_TEMPLATE;
  const title = t("ROUTE_ERROR_SCREEN.TITLE");

  // Once per error: StrictMode and config refetches re-run the effect with the same one.
  const reportedRef = useRef<{ error: unknown } | null>(null);
  useEffect(() => {
    if (reportedRef.current?.error === error) return;
    reportedRef.current = { error };
    reportOidcRouteError(error, pathname);
  }, [error, pathname]);

  return (
    <OidcAuthShell
      panelConfig={OIDC_ERROR_PANEL}
      showAnimationPanel={template.layout.showAnimationPanel}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={title}
      headingDimFirst={0}
      headingAlign="center"
      successTitle=""
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <PanelFailure message={title} />
      <div className="flex flex-col items-center gap-6 text-center oidc-animate-fade-up">
        <div className={`flex h-16 w-16 items-center justify-center rounded-full border ${TONE_CLASSES.danger.ring}`}>
          <AlertCircle className={`h-8 w-8 ${TONE_CLASSES.danger.icon}`} aria-hidden />
        </div>

        <div className="flex w-full flex-col gap-3">
          <button
            type="button"
            onClick={() => globalThis.location.reload()}
            className="oidc-sci-fi-btn flex w-full items-center justify-center"
          >
            {t("ROUTE_ERROR_SCREEN.TRY_AGAIN")}
          </button>
          <LoginReturnLink className="text-sm text-(--muted) underline-offset-4 hover:underline">
            {t("ROUTE_ERROR_SCREEN.BACK_TO_LOGIN")}
          </LoginReturnLink>
        </div>
      </div>
    </OidcAuthShell>
  );
};
