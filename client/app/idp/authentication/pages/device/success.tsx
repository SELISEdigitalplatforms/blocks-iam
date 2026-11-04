import { useLayoutEffect, useMemo, useRef } from "react";
import { useSearchParams } from "react-router";
import { Loader, XCircle } from "lucide-react";

import { OidcAuthShell, OidcFooter, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useT, type TFunction } from "@seliseblocks/genesis-os/i18n";
import { buildDeviceConsentPanel } from "./panel-config";

type Outcome = "approved" | "denied" | "expired" | "neutral";

const headingFor = (t: TFunction, outcome: Outcome): string => {
  const headings: Record<Outcome, string> = {
    approved: t("DEVICE_SUCCESS.FLOW_COMPLETE"),
    denied: t("DEVICE_SUCCESS.AUTHORIZATION_DECLINED"),
    expired: t("DEVICE_SUCCESS.SESSION_EXPIRED"),
    neutral: t("DEVICE_SUCCESS.FLOW_COMPLETE"),
  };
  return headings[outcome];
};

function resolveOutcome(raw: string | null): Outcome {
  if (raw === "approved") return "approved";
  if (raw === "denied") return "denied";
  if (raw === "expired") return "expired";
  return "neutral";
}

/* Rendered as the shell's children, so it sits below the anim Provider and can
   actually see the context. The decision already happened on the previous screen,
   so this jumps straight to the final panel/card state instead of replaying the
   multi-second "simulated request" cascade — nothing here is still in flight. */
function DeviceSuccessBody({ outcome }: Readonly<{ outcome: Outcome }>) {
  const { t } = useT("iam.device");
  const animCtx = useOidcAuthAnimation();
  const startedRef = useRef(false);

  useLayoutEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    if (outcome === "denied") {
      animCtx?.failAnimation(t("DEVICE_SUCCESS.AUTHORIZATION_DECLINED_LOWER"), { instant: true });
    } else if (outcome === "expired") {
      animCtx?.failAnimation(t("DEVICE_SUCCESS.DEVICE_CODE_EXPIRED"), { instant: true });
    } else {
      animCtx?.succeedAnimation({ instant: true });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (outcome === "denied" || outcome === "expired") {
    return (
      <div className="flex flex-col items-center text-center gap-3 oidc-animate-fade-up">
        <div
          className="w-14 h-14 rounded-full flex items-center justify-center"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <XCircle size={26} style={{ color: "var(--danger)" }} />
        </div>
        <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          {t("DEVICE_SUCCESS.CLOSE_WINDOW")}
        </p>
      </div>
    );
  }

  return (
    <p className="text-sm oidc-font-rajdhani text-center" style={{ color: "var(--muted)" }}>
      {t("DEVICE_SUCCESS.CLOSE_WINDOW")}
    </p>
  );
}

export function DeviceSuccessPage() {
  const { t } = useT("iam.device");
  const consentPanel = useMemo(() => buildDeviceConsentPanel(t), [t]);
  const [searchParams] = useSearchParams();
  const { data: oidcUiConfig } = useOidcUiConfig();
  const template = oidcUiConfig?.template;
  const outcome = useMemo<Outcome>(
    () => resolveOutcome(searchParams.get("outcome")),
    [searchParams],
  );

  const copy = useMemo(() => {
    switch (outcome) {
      case "approved":
        return {
          successTitle: t("DEVICE_SUCCESS.SUCCESS"),
          successSubtitle: t("DEVICE_SUCCESS.APPROVED_SUBTITLE"),
        };
      case "denied":
        return {
          successTitle: t("DEVICE_SUCCESS.AUTHORIZATION_DECLINED"),
          successSubtitle: t("DEVICE_SUCCESS.DENIED_SUBTITLE"),
        };
      case "expired":
        return {
          successTitle: t("DEVICE_SUCCESS.SESSION_EXPIRED"),
          successSubtitle: t("DEVICE_SUCCESS.EXPIRED_SUBTITLE"),
        };
      default:
        return {
          successTitle: t("DEVICE_SUCCESS.SUCCESS"),
          successSubtitle: t("DEVICE_SUCCESS.NEUTRAL_SUBTITLE"),
        };
    }
  }, [outcome, t]);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={consentPanel}
      showAnimationPanel={template.layout.showAnimationPanel}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={headingFor(t, outcome)}
      headingDimFirst={3}
      showCorners={false}
      successTitle={copy.successTitle}
      successSubtitle={copy.successSubtitle}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <DeviceSuccessBody outcome={outcome} />
    </OidcAuthShell>
  );
}
