import { useEffect, useState } from "react";
import { useAccountActivationCodeExpiration, useAccountResendActivation } from "@blocks-idp/iam/hooks/use-account";
import { AlertTriangle, CheckCircle2, Loader } from "lucide-react";
import { ActivationForm } from "./activation-form";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";
import { SciFiBackgroundOidc } from "../oidc/sci-fi-background-oidc";
import { Separator } from "@/components/ui-kits/separator/separator";
import "../oidc/sci-fi-oidc.css";

type ActivationProps = {
  code?: string;
  lang?: string;
  tenantId?: string;
};

function BlocksLogo() {
  return (
    <svg className="h-7 w-auto" viewBox="0 0 246 360" xmlns="http://www.w3.org/2000/svg" fill="var(--accent)" aria-hidden>
      <path d="M245.455 68.162V129.87L168.982 156.65V93.9637L245.455 68.162Z" />
      <path d="M240.389 62.3805L165.49 87.6573L5.30945 24.2563L85.3315 0L240.389 62.3805Z" />
      <path d="M161.797 93.8295V156.43L81.1141 122.607V188.07L0 152.738V29.6846L161.797 93.8295Z" />
      <path d="M76.4728 266.036L0 291.837V230.123L76.4728 203.329V266.036Z" />
      <path d="M160.122 360L5.07166 297.619L79.9639 272.343L240.144 335.742L160.122 360Z" />
      <path d="M245.454 330.315L83.6569 266.175V203.57L164.34 237.395V171.93L245.454 207.262V330.315Z" />
    </svg>
  );
}

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

  const [htmlTheme, setHtmlTheme] = useState<"dark" | "light">(() =>
    typeof document !== "undefined" && document.documentElement.classList.contains("dark")
      ? "dark"
      : "light"
  );

  useEffect(() => {
    const observer = new MutationObserver(() => {
      setHtmlTheme(document.documentElement.classList.contains("dark") ? "dark" : "light");
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
    return () => observer.disconnect();
  }, []);

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

  const headerBadge =
    activationError === "invalid"
      ? "Invalid Link"
      : activationError === "expired"
        ? "Link Expired"
        : "Account Activation";

  const headerSubtitle =
    activationError === "invalid"
      ? "This activation link is not valid"
      : activationError === "expired"
        ? "This activation link has expired"
        : "Complete your account setup";

  return (
    <div
      className="oidc-scifi-root min-h-screen overflow-hidden relative bg-[var(--bg)]"
      data-theme={htmlTheme}
    >
      <SciFiBackgroundOidc showCorners={false} />
      <main className="relative z-10 min-h-screen flex flex-col items-center justify-center px-4 gap-6">
        <div className="w-full max-w-lg rounded-2xl border border-[var(--border)] bg-[var(--node-bg)] p-10 backdrop-blur-[16px]">
          <div className="flex items-center gap-3 mb-8">
            <BlocksLogo />
            <Separator orientation="vertical" className="h-4 bg-[var(--border)]" />
            <span className="font-sans text-xs font-semibold tracking-[.18em] uppercase text-[var(--muted)]">
              {headerBadge}
            </span>
          </div>

          <h2 className="text-xl font-semibold mb-2 font-sans text-[var(--fg)]">
            Activate your account
          </h2>
          <p className="text-sm font-sans text-[var(--muted)] mb-6">
            {headerSubtitle}
          </p>

          {isActivationPending || isValidCode === null ? (
            <div className="flex items-center justify-center py-8">
              <Loader className="h-8 w-8 animate-spin text-[var(--accent)]" />
            </div>
          ) : activationError === null ? (
            <ActivationForm code={code ?? ""} tenantId={tenantId} />
          ) : activationError === "invalid" ? (
            <div className="flex flex-col items-center gap-3 py-4 text-center">
              <AlertTriangle className="h-10 w-10 text-amber-500" />
              <p className="text-sm text-[var(--muted)]">
                The activation code is invalid. Please check the link or request a
                new activation email from your administrator.
              </p>
              <a
                href="/login"
                className="oidc-sci-fi-link mt-2 text-sm font-medium"
              >
                Back to login
              </a>
            </div>
          ) : (
            <div className="flex flex-col items-center gap-3 py-4 text-center">
              <AlertTriangle className="h-10 w-10 text-amber-500" />
              <p className="text-sm text-[var(--muted)]">
                This activation link has expired and can&apos;t be used anymore.
                Please request a new link to complete your account activation.
              </p>
              <button
                type="button"
                onClick={handleResendActivation}
                disabled={!activationUserId || isResendPending || resendSuccess}
                className="oidc-sci-fi-btn mt-2 w-full flex items-center justify-center gap-2"
              >
                <span>{isResendPending ? "Sending..." : "Resend activation link"}</span>
              </button>
              {resendMessage && (
                <div className="flex items-center gap-2 text-sm">
                  {resendSuccess ? (
                    <CheckCircle2 className="h-4 w-4 text-success" />
                  ) : (
                    <AlertTriangle className="h-4 w-4 text-[var(--danger)]" />
                  )}
                  <span className={resendSuccess ? "text-success" : "text-[var(--danger)]"}>
                    {resendMessage}
                  </span>
                </div>
              )}
            </div>
          )}
        </div>
        <ModeToggle />
      </main>
    </div>
  );
};