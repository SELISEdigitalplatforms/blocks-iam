import { useOIDCContext } from "@/layouts/oidc-layout";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import {
  readSsoReturnUri,
  resolveLoginReturnTarget,
} from "@blocks-idp/authentication/utils/oidc-utils";
import { AlertCircle, CheckCircle2, Clock, type LucideIcon } from "lucide-react";
import { useEffect, useMemo } from "react";
import { useSearchParams } from "react-router";
import { useT, type TFunction } from "@seliseblocks/genesis-os/i18n";
import { OidcAuthShell, OidcFooter, useOidcAuthAnimation } from "./oidc-auth-shell";
import { OIDC_ERROR_PANEL } from "./oidc-panel-config";

type Tone = "danger" | "warn" | "success";

type ErrorCopy = {
  title: string;
  message: string;
  icon: LucideIcon;
  tone: Tone;
};

const TONE_CLASSES: Record<Tone, { icon: string; ring: string }> = {
  danger: { icon: "text-(--danger)", ring: "bg-(--danger-soft) border-(--danger-border)" },
  warn: { icon: "text-(--warn)", ring: "bg-(--warn-soft) border-(--warn-border)" },
  success: { icon: "text-(--success)", ring: "bg-(--success-soft) border-(--success-border)" },
};

const formatErrorCode = (code: string): string =>
  code
    .replace(/_/g, " ")
    .replace(/\b\w/g, (c) => c.toUpperCase());

// Plain words for the codes a user can actually hit. Anything else shows the server's own text.
const resolveCopy = (
  t: TFunction,
  errorCode: string | null,
  errorDescription: string | null,
): ErrorCopy => {
  switch (errorCode) {
    case "invalid_state":
      return {
        title: t("ERROR_SCREEN.SIGN_IN_EXPIRED_TITLE"),
        message: t("ERROR_SCREEN.INVALID_STATE_MESSAGE"),
        icon: Clock,
        tone: "warn",
      };
    case "oidc_flow_expired":
      return {
        title: t("ERROR_SCREEN.SIGN_IN_EXPIRED_TITLE"),
        message: t("ERROR_SCREEN.FLOW_EXPIRED_MESSAGE"),
        icon: Clock,
        tone: "warn",
      };
    case "state_already_used":
      return {
        title: t("ERROR_SCREEN.ALREADY_SIGNED_IN_TITLE"),
        message: t("ERROR_SCREEN.STATE_ALREADY_USED_MESSAGE"),
        icon: CheckCircle2,
        tone: "success",
      };
    default:
      break;
  }

  if (errorCode || errorDescription) {
    return {
      title: t("ERROR_SCREEN.SIGN_IN_FAILED_TITLE"),
      message: errorDescription || t("ERROR_SCREEN.SIGN_IN_FAILED_MESSAGE"),
      icon: AlertCircle,
      tone: "danger",
    };
  }

  return {
    title: t("ERROR_SCREEN.ACCESS_BLOCKED_TITLE"),
    message: t("ERROR_SCREEN.ACCESS_BLOCKED_MESSAGE"),
    icon: AlertCircle,
    tone: "danger",
  };
};

/** Puts the side panel in its failed state, so it never says "enter your email and password" here. */
const PanelFailure = ({ message }: Readonly<{ message: string }>) => {
  const failAnimation = useOidcAuthAnimation()?.failAnimation;

  useEffect(() => {
    void failAnimation?.(message, { instant: true });
    // Run once per message: the context hands out a new function on every render.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [message]);

  return null;
};

export const OIDCErrorScreen = () => {
  const { t } = useT("iam.oidc");
  const { tenantId } = useOIDCContext();
  const [searchParams] = useSearchParams();
  // Falls back to the default template when the config call fails, so the error page always renders.
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId || searchParams.get("tenant_id") || undefined);
  const template = oidcUiConfig.template ?? DEFAULT_OIDC_UI_TEMPLATE;

  const errorCode = searchParams.get("error");
  const errorDescription = searchParams.get("error_description");
  const copy = resolveCopy(t, errorCode, errorDescription);
  const tone = TONE_CLASSES[copy.tone];
  const Icon = copy.icon;

  const fallbackRedirectUri = useMemo(() => readSsoReturnUri(), []);
  const returnsToApplication = resolveLoginReturnTarget(true, {
    preferOidcLogin: true,
    fallbackRedirectUri,
  }).external;

  return (
    <OidcAuthShell
      panelConfig={OIDC_ERROR_PANEL}
      showAnimationPanel={template.layout.showAnimationPanel}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={copy.title}
      headingDimFirst={0}
      headingAlign="center"
      successTitle=""
      successSubtitle=""
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <PanelFailure message={copy.title} />
      <div className="flex flex-col items-center gap-6 text-center oidc-animate-fade-up">
        <div
          className={`flex h-16 w-16 items-center justify-center rounded-full border ${tone.ring}`}
        >
          <Icon className={`h-8 w-8 ${tone.icon}`} aria-hidden />
        </div>

        <p className="max-w-sm text-sm leading-relaxed text-(--fg)">{copy.message}</p>

        <LoginReturnLink
          preferOidcLogin
          fallbackRedirectUri={fallbackRedirectUri}
          className="oidc-sci-fi-btn flex w-full items-center justify-center"
        >
          {returnsToApplication ? t("ERROR_SCREEN.BACK_TO_APPLICATION") : t("ERROR_SCREEN.BACK_TO_SIGN_IN")}
        </LoginReturnLink>

        <div className="flex flex-col items-center gap-1 text-xs text-(--muted)">
          <p>{t("ERROR_SCREEN.CONTACT_ADMIN")}</p>
          {errorCode && (
            <p>
              {t("ERROR_SCREEN.ERROR_CODE_LABEL")} <span className="font-mono">{formatErrorCode(errorCode)}</span>
            </p>
          )}
        </div>
      </div>
    </OidcAuthShell>
  );
};
