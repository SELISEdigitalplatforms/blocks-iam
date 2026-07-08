import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AlertTriangle, CheckCircle2, Loader } from "lucide-react";
import { ActivationForm } from "./activation-form";
import { OidcAuthShell } from "../oidc/oidc-auth-shell";
import { ACTIVATE_PANEL } from "../oidc/oidc-panel-config";
import { useAccountActivationCodeExpiration, useAccountResendActivation } from "@blocks-idp/iam/hooks/use-account";

type ActivationProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

export const Activation = ({ code, tenantId }: ActivationProps) => {
  const {
    isPending: isActivationPending,
    mutateAsync: activationCodeValidation,
  } = useAccountActivationCodeExpiration();
  const { mutateAsync: resendActivationLink, isPending: isResendPending } =
    useAccountResendActivation();

  const [isValidCode, setIsValidCode] = useState<boolean | null>(null);
  const [activationError, setActivationError] = useState<"invalid" | "expired" | null>(null);
  const [activationUserId, setActivationUserId] = useState<string | null>(null);
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
        : "Activate Your Account";

  const headingDimFirst = activationError ? 2 : 2;

  return (
    <OidcAuthShell
      panelConfig={ACTIVATE_PANEL}
      heading={heading}
      headingDimFirst={headingDimFirst}
      successTitle="Account Activated"
      successSubtitle="Your account is ready. Redirecting to login…"
      showCorners={false}
    >
      {isActivationPending || isValidCode === null ? (
        <div className="flex items-center justify-center py-8">
          <Loader size={28} className="animate-spin" style={{ color: "var(--accent)" }} />
        </div>
      ) : activationError === null ? (
        <ActivationForm code={code ?? ""} tenantId={tenantId} />
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
          <Link
            to="/login"
            className="oidc-sci-fi-btn"
            style={{ textDecoration: "none", display: "inline-block", textAlign: "center", padding: "10px 20px" }}
          >
            Back to login
          </Link>
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