import { getRuntimeEnv } from "@/lib/runtime-env";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { useForm } from "react-hook-form";
import {
  resetPasswordFormSchema,
  ResetPasswordFormValuesType,
  resetPasswordFormDefaultValue,
} from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "react-router";
import { Captcha } from "@/components/captcha";
import { useEffect, useRef, useState } from "react";
import { useAccountResetPassword } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { PasswordStrengthChecker } from "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ArrowRight, Eye, EyeOff, Loader } from "lucide-react";
import { useOidcAuthAnimation } from "../oidc/oidc-auth-shell";
import { appendTenantId, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

type ResetPasswordFormProps = { code: string; tenantId?: string };

export const ResetPasswordForm = ({ code, tenantId }: ResetPasswordFormProps) => {
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);
  const [requirementsMet, setRequirementsMet] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);

  const form = useForm<ResetPasswordFormValuesType>({
    defaultValues: resetPasswordFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(resetPasswordFormSchema),
  });

  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig(tenantId);
  const googleSiteKey =
    oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const { captcha, code: captchaCode, reset: resetCaptcha } = useCaptcha({
    siteKey: googleSiteKey,
    type: oidcUiConfig?.captcha?.provider === "hcaptcha" ? "hCaptcha" : "reCaptcha-v2-checkbox",
    generator: oidcUiConfig?.captcha?.generator,
  });

  const { isPending, mutateAsync } = useAccountResetPassword();
  const { isValid } = form.formState;
  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");
  const logoutFromAllDevices = form.watch("logoutFromAllDevices");

  /* Inject PasswordStrengthChecker into the right panel's idle slot */
  const setPanelIdleSlot = animCtx?.setPanelIdleSlot;
  useEffect(() => {
    setPanelIdleSlot?.(
      <PasswordStrengthChecker
        password={password}
        confirmPassword={confirmPassword}
        onRequirementsMet={setRequirementsMet}
      />
    );
    return () => { setPanelIdleSlot?.(null); };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [password, confirmPassword, setPanelIdleSlot]);

  const isAuthenticating =
    isPending ||
    animCtx?.phase === "submitting" ||
    animCtx?.phase === "succeeded";

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("oidc-animate-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("oidc-animate-shake");
  }

  useEffect(() => {
    if (!isValid && !requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, requirementsMet, resetCaptcha]);

  if (!oidcUiConfig?.template) return null;
  const resetPasswordCopy = oidcUiConfig.template.pages.resetPassword;

  const onSubmitHandler = async (values: ResetPasswordFormValuesType) => {
    setServerError(null);
    animCtx?.startAnimation();
    try {
      const res = await mutateAsync({
        code,
        captchaCode,
        logoutFromAllDevices: values.logoutFromAllDevices,
        password: values.password,
        tenantId,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Reset failed")
          : (res.errors as string) || "Reset failed";
        setServerError(msg);
        shake();
        await animCtx?.failAnimation(msg);
        return;
      }
      await animCtx?.succeedAnimation();
      // Same as activation: keep the recovery link's OIDC context so "Log in" returns
      // the user to the application that sent them here.
      navigate(appendTenantId(buildOIDCNavigationUrl("/oidc/reset-password-success"), tenantId));
    } catch (error) {
      resetCaptcha();
      shake();
      if (isErrorWithErrors(error)) {
        const msg = Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : error.errors && typeof error.errors === "object"
          ? (Object.values(error.errors as Record<string, string>)[0] ?? "Something went wrong")
          : (error.errors as unknown as string) || "Something went wrong";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      } else {
        const msg = "Something went wrong";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      }
    }
  };

  return (
    <div className="flex flex-col gap-5">
      <form
        ref={formRef}
        onSubmit={form.handleSubmit(onSubmitHandler, shake)}
        onInput={() => {
          if (serverError) setServerError(null);
          if (animCtx?.phase === "failed") animCtx?.resetAnimation();
        }}
        className="flex flex-col gap-5"
      >
        <div className="flex flex-col gap-2">
          <label className="oidc-sci-fi-label">{resetPasswordCopy.passwordLabel}</label>
          <div className="relative">
            <input
              type={showPassword ? "text" : "password"}
              placeholder="••••••••"
              autoComplete="new-password"
              className="oidc-sci-fi-input"
              style={{ paddingRight: "2.75rem" }}
              aria-invalid={!!form.formState.errors.password}
              disabled={isAuthenticating}
              {...form.register("password")}
            />
            <button
              type="button"
              tabIndex={-1}
              onClick={() => setShowPassword(v => !v)}
              className="absolute right-3 top-1/2 -translate-y-1/2"
              style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
              aria-label={showPassword ? "Hide password" : "Show password"}
            >
              {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
            </button>
          </div>
          {form.formState.errors.password && (
            <p className="text-xs" style={{ color: "var(--danger)" }}>{form.formState.errors.password.message}</p>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <label className="oidc-sci-fi-label">{resetPasswordCopy.confirmPasswordLabel}</label>
          <div className="relative">
            <input
              type={showConfirmPassword ? "text" : "password"}
              placeholder="••••••••"
              autoComplete="new-password"
              className="oidc-sci-fi-input"
              style={{ paddingRight: "2.75rem" }}
              aria-invalid={!!form.formState.errors.confirmPassword}
              disabled={isAuthenticating}
              {...form.register("confirmPassword")}
            />
            <button
              type="button"
              tabIndex={-1}
              onClick={() => setShowConfirmPassword(v => !v)}
              className="absolute right-3 top-1/2 -translate-y-1/2"
              style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
              aria-label={showConfirmPassword ? "Hide password" : "Show password"}
            >
              {showConfirmPassword ? <EyeOff size={16} /> : <Eye size={16} />}
            </button>
          </div>
          {form.formState.errors.confirmPassword && (
            <p className="text-xs" style={{ color: "var(--danger)" }}>{form.formState.errors.confirmPassword.message}</p>
          )}
        </div>

        <div
          className="rounded-lg py-2.5 px-3 flex items-center justify-between gap-3"
          style={{ border: "1px solid var(--border)", background: "var(--accent-softer)" }}
        >
          <p className="text-sm font-medium" style={{ color: "var(--fg)", fontFamily: "system-ui, sans-serif" }}>
            {resetPasswordCopy.logoutFromDevicesLabel}
          </p>
          <Switch
            checked={logoutFromAllDevices ?? true}
            onCheckedChange={(val) => form.setValue("logoutFromAllDevices", val)}
          />
        </div>

        {captchaEnabled && isValid && requirementsMet && <Captcha {...captcha} />}

        {serverError && (
          <p className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>
            {serverError}
          </p>
        )}

        <button
          type="submit"
          disabled={isAuthenticating || (captchaEnabled && !captchaCode) || !isValid || !requirementsMet}
          className="oidc-sci-fi-btn mt-1 w-full flex items-center justify-center gap-2"
        >
          {isAuthenticating ? (
            <><Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} /><span>Resetting…</span></>
          ) : (
            <><span>{resetPasswordCopy.submitButton}</span><ArrowRight size={16} /></>
          )}
        </button>
      </form>

      <LoginReturnLink className="oidc-sci-fi-link text-sm text-center">
        Back to login
      </LoginReturnLink>
    </div>
  );
};
