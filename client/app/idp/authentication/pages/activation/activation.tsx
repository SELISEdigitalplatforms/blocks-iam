import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Link } from "react-router-dom";
import {
  useAccountActivationCodeExpiration,
  useAccountResendActivation,
} from "@blocks-idp/iam/hooks/use-account";
import { AlertTriangle, CheckCircle2, KeyRound, Lock, Loader, ShieldCheck } from "lucide-react";
import { useEffect, useState } from "react";
import { ActivationForm } from "./activation-form";
import { Logo } from "@/components/logo";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

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

        if (res.errors != null) {
          setActivationError("invalid");
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
        } else if (res.userId != null) {
          setActivationError("expired");
          setActivationUserId(res.userId);
          setResendMessage(null);
          setResendSuccess(false);
        } else {
          setActivationError(null);
          setActivationUserId(null);
          setResendMessage(null);
          setResendSuccess(false);
        }

        setIsValidCode(res.isSuccess);
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
    <div className="fixed inset-0 z-50 flex flex-col bg-[hsl(var(--surface-app))]">
      {/* Background decorations */}
      <div className="pointer-events-none absolute inset-0 overflow-hidden">
        <div className="absolute -left-40 -top-40 h-96 w-96 rounded-full bg-primary/5 blur-3xl" />
        <div className="absolute -bottom-40 right-10 h-80 w-80 rounded-full bg-primary/5 blur-3xl" />
        <div className="absolute left-1/2 top-1/4 h-64 w-64 -translate-x-1/2 rounded-full bg-primary/3 blur-3xl" />
      </div>

      {/* Header */}
      <header className="relative z-10 flex items-center px-6 py-5 xl:px-[154px]">
        <Link to="/login" className="hover:opacity-80 transition-opacity">
          <Logo width={120} height={52} />
        </Link>
        <div className="absolute right-6 top-5 xl:right-[154px]">
          <ModeToggle />
        </div>
      </header>

      {/* Main */}
      <main className="relative z-10 min-h-0 flex-1 overflow-y-auto">
        <div className="flex min-h-full items-center justify-center px-6 py-8">
        <div className="w-full max-w-[420px]">
          {/* Card */}
          <div className="overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-md">
            {/* Card header */}
            <div className="relative overflow-hidden rounded-t-2xl bg-primary px-6 py-7">
              <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
              <div className="absolute -bottom-6 right-4 h-20 w-20 rounded-full bg-white/5" />
              <span className="relative inline-flex items-center rounded-full bg-white/15 px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-widest text-primary-foreground/80">
                {headerBadge}
              </span>
              <div className="relative mt-3">
                <h1 className="text-lg font-bold leading-tight text-primary-foreground">
                  Blocks Identity Provider
                </h1>
                <p className="mt-0.5 text-xs text-primary-foreground/70">
                  {headerSubtitle}
                </p>
              </div>
            </div>

            {/* Card body */}
            <div className="p-6">
              {isActivationPending || isValidCode === null ? (
                <div className="flex items-center justify-center py-8">
                  <Loader className="h-8 w-8 animate-spin text-primary" />
                </div>
              ) : activationError === null ? (
                <ActivationForm code={code ?? ""} tenantId={tenantId} />
              ) : activationError === "invalid" ? (
                <div className="flex flex-col items-center gap-3 py-4 text-center">
                  <AlertTriangle className="h-10 w-10 text-amber-500" />
                  <p className="text-sm text-muted-foreground">
                    The activation code is invalid. Please check the link or request a
                    new activation email from your administrator.
                  </p>
                  <Link
                    to="/login"
                    className="mt-2 text-sm font-medium text-primary hover:underline"
                  >
                    Back to login
                  </Link>
                </div>
              ) : (
                <div className="flex flex-col items-center gap-3 py-4 text-center">
                  <AlertTriangle className="h-10 w-10 text-amber-500" />
                  <p className="text-sm text-muted-foreground">
                    This activation link has expired and can&apos;t be used anymore.
                    Please request a new link to complete your account activation.
                  </p>
                  <Button
                    className="mt-2 w-full rounded"
                    onClick={handleResendActivation}
                    disabled={!activationUserId || isResendPending || resendSuccess}
                  >
                    {isResendPending ? "Sending..." : "Resend activation link"}
                  </Button>
                  {resendMessage && (
                    <div className="flex items-center gap-2 text-sm">
                      {resendSuccess ? (
                        <CheckCircle2 className="h-4 w-4 text-success" />
                      ) : (
                        <AlertTriangle className="h-4 w-4 text-destructive" />
                      )}
                      <span className={resendSuccess ? "text-success" : "text-destructive"}>
                        {resendMessage}
                      </span>
                    </div>
                  )}
                </div>
              )}
            </div>
          </div>

          {/* Trust row */}
          <div className="mt-5 flex items-center justify-center gap-4">
            <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
              <ShieldCheck className="h-3.5 w-3.5 text-primary/60" />
              MFA Ready
            </div>
            <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
            <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
              <KeyRound className="h-3.5 w-3.5 text-primary/60" />
              SSO Enabled
            </div>
            <span className="h-3 w-px bg-[hsl(var(--border-default))]" />
            <div className="flex items-center gap-1.5 text-[11px] text-[hsl(var(--low-emphasis))]">
              <Lock className="h-3.5 w-3.5 text-primary/60" />
              Encrypted
            </div>
          </div>

          {/* Copyright */}
          <p className="mt-4 text-center text-[11px] text-[hsl(var(--low-emphasis))]">
            © {new Date().getFullYear()} SELISE Digital Platforms. All rights reserved.
          </p>
        </div>
        </div>
      </main>
    </div>
  );
};
