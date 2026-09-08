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

  const [isValidCode, setIsValidCode] = useState<boolean | null>(null);
  const [activationError, setActivationError] = useState<
    "invalid" | "expired" | "already-active" | null
  >(null);
  const [activationUserId, setActivationUserId] = useState<string | null>(null);
  const [knownName, setKnownName] = useState<{ firstName: string; lastName: string }>({
    firstName: "",
    lastName: "",
  });
  const [resendMessage, setResendMessage] = useState<string | null>(null);
  const [resendSuccess, setResendSuccess] = useState(false);

  useEffect(() => {
    if (!code) {
      setActivationError("invalid");
      setActivationUserId(null);
      setResendMessage(null);
      setResendSuccess(false);
      setIsValidCode(false);
      return;
    }

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

        setActivationError(
          status === "AlreadyActivated"
            ? "already-active"
            : status === "Expired"
              ? "expired"
              : "invalid",
        );
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
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-[var(--bg)]">
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
        setResendMessage("A new activation link has been sent to your email.");
      } else {
        setResendSuccess(false);
        setResendMessage("Failed to resend activation link. Please try again later.");
      }
    } catch (error) {
      setResendSuccess(false);
      setResendMessage(
        error instanceof Error ? error.message : "Failed to resend activation link.",
      );
    }
  };

  const heading =
    activationError === "invalid"
      ? "Invalid Activation Link"
      : activationError === "expired"
        ? "Link Expired"
        : activationError === "already-active"
          ? "Already Activated"
          : template.pages.activation.heading;

  const headingDimFirst = 2;

  return (
    <OidcAuthShell
      panelConfig={getActivatePanel(collectPasswordOnActivation)}
      theme={template.theme}
      logoUrl={template.branding.logoUrl}
      brandName={template.branding.brandName}
      heading={heading}
      headingDimFirst={headingDimFirst}
      headingAlign={heading === "Invalid Activation Link" ? "center" : "left"}
      successTitle={template.pages.activation.successTitle}
      successSubtitle={template.pages.activation.successSubtitle}
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      {isActivationPending || isValidCode === null || isUiConfigLoading ? (
        <div className="flex items-center justify-center py-8">
          <Loader size={28} className="animate-spin" style={{ color: "var(--accent)" }} />
        </div>
      ) : activationError === null ? (
        <ActivationForm
          code={code ?? ""}
          tenantId={tenantId}
          firstName={knownName.firstName}
          lastName={knownName.lastName}
          collectPassword={collectPasswordOnActivation}
        />
      ) : activationError === "invalid" ? (
        <div className="flex flex-col items-center gap-3 py-2 text-center">
          <div
            className="w-12 h-12 rounded-full flex items-center justify-center"
            style={{ background: "rgba(234,179,8,.1)", border: "1px solid rgba(234,179,8,.25)" }}
          >
            <AlertTriangle size={22} style={{ color: "var(--warn)" }} />
          </div>
          <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
            The activation code is invalid. Please check the link or request a
            new activation email from your administrator.
          </p>
          <LoginReturnLink className="oidc-sci-fi-btn inline-block px-5 py-2.5 text-center no-underline">
            Back to login
          </LoginReturnLink>
        </div>
      ) : activationError === "already-active" ? (
        <div className="flex flex-col items-center gap-3 py-2 text-center">
          <div
            className="w-12 h-12 rounded-full flex items-center justify-center"
            style={{ background: "rgba(34,197,94,.1)", border: "1px solid rgba(34,197,94,.25)" }}
          >
            <CheckCircle2 size={22} style={{ color: "var(--success)" }} />
          </div>
          <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
            This account is already active, so there is nothing left to confirm. Sign in to
            continue -- use the forgot-password link if you have not set a password yet.
          </p>
          <LoginReturnLink className="oidc-sci-fi-btn inline-block px-5 py-2.5 text-center no-underline">
            Go to login
          </LoginReturnLink>
        </div>
      ) : (
        <div className="flex flex-col items-center gap-3 py-2 text-center">
          <div
            className="w-12 h-12 rounded-full flex items-center justify-center"
            style={{ background: "rgba(234,179,8,.1)", border: "1px solid rgba(234,179,8,.25)" }}
          >
            <AlertTriangle size={22} style={{ color: "var(--warn)" }} />
          </div>
          <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
            This activation link has expired and can&apos;t be used anymore.
            Please request a new link to complete your account activation.
          </p>
          <button
            type="button"
            onClick={handleResendActivation}
            disabled={!activationUserId || isResendPending || resendSuccess}
            className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
          >
            {isResendPending ? "Sending..." : "Resend activation link"}
          </button>
          {resendMessage && (
            <div className="flex items-center gap-2 text-sm">
              {resendSuccess ? (
                <CheckCircle2 className="h-4 w-4 text-[var(--success)]" />
              ) : (
                <AlertTriangle className="h-4 w-4 text-[var(--danger)]" />
              )}
              <span
                className={resendSuccess ? "text-[var(--success)]" : "text-[var(--danger)]"}
                style={{ fontFamily: "system-ui, sans-serif" }}
              >
                {resendMessage}
              </span>
            </div>
          )}
        </div>
      )}
    </OidcAuthShell>
  );
};
