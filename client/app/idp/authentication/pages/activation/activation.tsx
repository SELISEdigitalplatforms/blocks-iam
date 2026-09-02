import { useEffect, useState } from "react";
import { AlertTriangle, CheckCircle2, Loader } from "lucide-react";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { ActivationForm } from "./activation-form";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { ACTIVATE_PANEL } from "../oidc/oidc-panel-config";
import { useAccountActivationCodeExpiration, useAccountResendActivation } from "@blocks-idp/iam/hooks/use-account";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";

type ActivationProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

export const Activation = ({ code, tenantId }: ActivationProps) => {
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE;
  const {
    isPending: isActivationPending,
    mutateAsync: activationCodeValidation,
  } = useAccountActivationCodeExpiration();
  const { mutateAsync: resendActivationLink, isPending: isResendPending } =
    useAccountResendActivation();

  const [isValidCode, setIsValidCode] = useState<boolean | null>(null);
  const [activationError, setActivationError] = useState<"invalid" | "expired" | null>(null);
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

        if (res.isSuccess && res.userId) {
          setActivationError(null);
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
          // Self-service signups already supplied these; invites return them empty.
          setKnownName({
            firstName: res.firstName ?? "",
            lastName: res.lastName ?? "",
          });
        } else if (res.errors) {
          setActivationError("invalid");
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
        } else {
          setActivationError("expired");
          setActivationUserId(res.userId);
          setResendMessage(null);
          setResendSuccess(false);
        }

        setIsValidCode(!!res.isSuccess);
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
        : template.pages.activation.heading;

  const headingDimFirst = 2;

  return (
    <OidcAuthShell
      panelConfig={ACTIVATE_PANEL}
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
      {isActivationPending || isValidCode === null ? (
        <div className="flex items-center justify-center py-8">
          <Loader size={28} className="animate-spin" style={{ color: "var(--accent)" }} />
        </div>
      ) : activationError === null ? (
        <ActivationForm
          code={code ?? ""}
          tenantId={tenantId}
          firstName={knownName.firstName}
          lastName={knownName.lastName}
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
