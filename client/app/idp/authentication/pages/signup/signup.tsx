import { SignupForm } from "./signup-form";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { useGetSignUpSetting } from "@blocks-idp/iam/hooks/use-user";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { KeyRound, Lock, Loader, ShieldCheck } from "lucide-react";
import { Logo } from "@/components/logo";
import { ModeToggle } from "@/components/mode-toggle/mode-toggle";

export const Signup = () => {
  const projectKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const { data: signUpSetting, isLoading: isSignUpSettingLoading } = useGetSignUpSetting({ projectKey });

  const isLoading = isLoginOptionLoading || isSignUpSettingLoading;

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
        <Logo width={120} height={52} />
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
                Create Account
              </span>
              <div className="relative mt-3">
                <h1 className="text-lg font-bold leading-tight text-primary-foreground">
                  Blocks Identity Provider
                </h1>
                <p className="mt-0.5 text-xs text-primary-foreground/70">
                  Create your account to get started
                </p>
              </div>
            </div>

            {/* Card body */}
            <div className="p-6">
              {isLoading ? (
                <div className="flex items-center justify-center py-8">
                  <Loader className="h-8 w-8 animate-spin text-primary" />
                </div>
              ) : !loginOption || loginOption.allowedGrantTypes?.length < 1 || !signUpSetting ? null : (
                <SignupForm
                  loginOption={loginOption}
                  emailSignUpEnabled={signUpSetting?.IsEmailPasswordSignUpEnabled || false}
                  ssoSignUpEnabled={signUpSetting?.IsSSoSignUpEnabled || false}
                />
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
