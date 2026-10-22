import { useEffect, useState } from "react";
import { useNavigate } from "react-router";
import { AlertTriangle, CheckCircle2, Loader } from "lucide-react";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { ActivationForm } from "./activation-form";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { getActivatePanel } from "../oidc/oidc-panel-config";
import { useAccountActivationCodeExpiration, useAccountResendActivation } from "@blocks-idp/iam/hooks/use-account";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { appendTenantId, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { resolveActivationCodeStatus } from "./activation-status";

type ActivationError = "invalid" | "expired" | "already-active" | null;

const errorForStatus = (status: ReturnType<typeof resolveActivationCodeStatus>): ActivationError => {
  if (status === "AlreadyActivated") return "already-active";
  if (status === "Expired") return "expired";
  return "invalid";
};

type ActivationCopy = NonNullable<
  NonNullable<ReturnType<typeof useOidcUiConfig>["data"]>["template"]
>["pages"]["activation"];

const headingForError = (error: ActivationError, copy: ActivationCopy) => {
  if (error === "invalid") return copy.invalidHeading;
  if (error === "expired") return copy.expiredHeading;
  if (error === "already-active") return copy.alreadyActiveHeading;
  return copy.heading;
};

const NoticeIcon = ({ children }: Readonly<{ children: React.ReactNode }>) => (
  <div
    className="w-12 h-12 rounded-full flex items-center justify-center"
    style={{ background: "var(--accent-soft)", border: "1px solid var(--border-strong)" }}
  >
    {children}
  </div>
);

const NoticeMessage = ({ children }: Readonly<{ children: React.ReactNode }>) => (
  <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
    {children}
  </p>
);

type ResendState = Readonly<{
  onResend: () => void;
  disabled: boolean;
  isPending: boolean;
  message: string | null;
  success: boolean;
}>;

type ActivationBodyArgs = Readonly<{
  activationError: ActivationError;
  copy: ActivationCopy;
  code?: string;
  tenantId?: string;
  knownName: { firstName: string; lastName: string };
  collectPassword: boolean;
  resend: ResendState;
}>;

const renderActivationBody = ({
  activationError,
  copy,
  code,
  tenantId,
  knownName,
  collectPassword,
  resend,
}: ActivationBodyArgs) => {
  if (activationError === null) {
    return (
      <ActivationForm
        code={code ?? ""}
        tenantId={tenantId}
        firstName={knownName.firstName}
        lastName={knownName.lastName}
        collectPassword={collectPassword}
      />
    );
  }
  if (activationError === "invalid") {
    return (
      <div className="flex flex-col items-center gap-3 py-2 text-center">
        <NoticeIcon>
          <AlertTriangle size={22} style={{ color: "var(--danger)" }} />
        </NoticeIcon>
        <NoticeMessage>{copy.invalidMessage}</NoticeMessage>
        <LoginReturnLink className="oidc-sci-fi-btn inline-block px-5 py-2.5 text-center no-underline">
          {copy.backToLoginButton}
        </LoginReturnLink>
      </div>
    );
  }
  if (activationError === "already-active") {
    return (
      <div className="flex flex-col items-center gap-3 py-2 text-center">
        <NoticeIcon>
          <CheckCircle2 size={22} style={{ color: "var(--success)" }} />
        </NoticeIcon>
        <NoticeMessage>{copy.alreadyActiveMessage}</NoticeMessage>
        <LoginReturnLink className="oidc-sci-fi-btn inline-block px-5 py-2.5 text-center no-underline">
          {copy.loginButton}
        </LoginReturnLink>
      </div>
    );
  }
  return (
    <div className="flex flex-col items-center gap-3 py-2 text-center">
      <NoticeIcon>
        <AlertTriangle size={22} style={{ color: "var(--danger)" }} />
      </NoticeIcon>
      <NoticeMessage>{copy.expiredMessage}</NoticeMessage>
      <button
        type="button"
        onClick={resend.onResend}
        disabled={resend.disabled}
        className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
      >
        {resend.isPending ? copy.activatingButton : copy.resendButton}
      </button>
      {resend.message && (
        <div className="flex items-center gap-2 text-sm">
          {resend.success ? (
            <CheckCircle2 className="h-4 w-4 text-(--success)" />
          ) : (
            <AlertTriangle className="h-4 w-4 text-(--danger)" />
          )}
          <span
            className={resend.success ? "text-(--success)" : "text-(--danger)"}
            style={{ fontFamily: "system-ui, sans-serif" }}
          >
            {resend.message}
          </span>
        </div>
      )}
    </div>
  );
};

type ActivationProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

export const Activation = ({ code, tenantId }: ActivationProps) => {
  const navigate = useNavigate();
  // `collectPasswordOnActivation` decides whether this page is a password form or a plain
  // confirmation, so the form is held back until the configuration query has settled --
  // the fallback template makes `template` truthy immediately, loaded or not.
  const {
    data: oidcUiConfig,
    collectPasswordOnActivation,
    isLoading: isUiConfigLoading,
  } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;
  const {
    isPending: isActivationPending,
    mutateAsync: activationCodeValidation,
  } = useAccountActivationCodeExpiration();
  const { mutateAsync: resendActivationLink, isPending: isResendPending } =
    useAccountResendActivation();

  // A link without a code can never validate, so that state is known before any request.
  const [isValidCode, setIsValidCode] = useState<boolean | null>(() => (code ? null : false));
  const [activationError, setActivationError] = useState<ActivationError>(
    () => (code ? null : "invalid"));
  const [activationUserId, setActivationUserId] = useState<string | null>(null);
  const [knownName, setKnownName] = useState<{ firstName: string; lastName: string }>({
    firstName: "",
    lastName: "",
  });
  const [resendMessage, setResendMessage] = useState<string | null>(null);
  const [resendSuccess, setResendSuccess] = useState(false);

  // The code dropping out of the URL after mount is settled during render, the same way.
  const [settledCode, setSettledCode] = useState(code);
  if (settledCode !== code) {
    setSettledCode(code);
    if (!code) {
      setActivationError("invalid");
      setActivationUserId(null);
      setResendMessage(null);
      setResendSuccess(false);
      setIsValidCode(false);
    }
  }

  useEffect(() => {
    if (!code) return;

    const validateCode = async () => {
      try {
        const res = await activationCodeValidation({
          activationCode: code,
          tenantId,
        });

        const status = resolveActivationCodeStatus(res);

        setResendMessage(null);
        setResendSuccess(false);
        setActivationUserId(null);

        if (status === "Valid") {
          setActivationError(null);
          // Self-service signups already supplied these; invites return them empty.
          setKnownName({
            firstName: res.firstName ?? "",
            lastName: res.lastName ?? "",
          });
          setIsValidCode(true);
          return;
        }

        // Expired is the one state a resend can rescue, and it is the only one that carries a
        // user id to resend to.
        if (status === "Expired") setActivationUserId(res.userId);

        setActivationError(errorForStatus(status));
        setIsValidCode(false);
      } catch {
        setActivationError("invalid");
        setActivationUserId(null);
        setResendMessage(null);
        setResendSuccess(false);
        setIsValidCode(false);
      }
    };

    validateCode();
  }, [code, tenantId, activationCodeValidation]);

  // An account that is already active has nothing left to confirm. Where the page activates on
  // its own, the usual way to arrive here is an email scanner having followed the link first, so
  // show the finished job rather than a dead end. Kept out of the effect above so it can wait for
  // the configuration to settle without validating the code a second time.
  const alreadyActive = activationError === "already-active";
  useEffect(() => {
    if (!alreadyActive || isUiConfigLoading || collectPasswordOnActivation) return;

    navigate(appendTenantId(buildOIDCNavigationUrl("/oidc/activate-success"), tenantId), {
      replace: true,
    });
  }, [alreadyActive, isUiConfigLoading, collectPasswordOnActivation, navigate, tenantId]);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  const handleResendActivation = async () => {
    if (!activationUserId || isResendPending) return;

    try {
      setResendMessage(null);
      setResendSuccess(false);

      const response = await resendActivationLink({
        userId: activationUserId,
        tenantId,
      });

      if (response?.isSuccess) {
        setResendSuccess(true);
        setResendMessage(template.pages.activation.resendSuccessMessage);
      } else {
        setResendSuccess(false);
        setResendMessage(template.pages.activation.resendFailureMessage);
      }
    } catch (error) {
      setResendSuccess(false);
      setResendMessage(
        error instanceof Error ? error.message : template.pages.activation.resendFailureMessage,
      );
    }
  };

  const heading = headingForError(activationError, template.pages.activation);

  const headingDimFirst = 2;

  return (
    <OidcAuthShell
      panelConfig={getActivatePanel(collectPasswordOnActivation)}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={heading}
      headingDimFirst={headingDimFirst}
      headingAlign={activationError === "invalid" ? "center" : "left"}
      successTitle={template.pages.activation.successTitle}
      successSubtitle={template.pages.activation.successSubtitle}
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      {isActivationPending || isValidCode === null || isUiConfigLoading ? (
        <div className="flex items-center justify-center py-8">
          <Loader size={28} className="animate-spin" style={{ color: "var(--accent)" }} />
        </div>
      ) : (
        renderActivationBody({
          activationError,
          copy: template.pages.activation,
          code,
          tenantId,
          knownName,
          collectPassword: collectPasswordOnActivation,
          resend: {
            onResend: () => {
              void handleResendActivation();
            },
            disabled: !activationUserId || isResendPending || resendSuccess,
            isPending: isResendPending,
            message: resendMessage,
            success: resendSuccess,
          },
        })
      )}
    </OidcAuthShell>
  );
};
