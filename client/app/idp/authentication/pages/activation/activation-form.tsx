import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { activationFormDefaultValue, activationFormSchema } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Captcha } from "@/components/captcha";
import { useNavigate } from "react-router-dom";
import { useEffect, useRef, useState } from "react";
import { useAccountActivation } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { ArrowRight, Eye, EyeOff, Loader } from "lucide-react";
import { useOidcAuthAnimation } from "../oidc/oidc-auth-shell";

type ActivationFormProps = {
  code: string;
  tenantId?: string;
};

export const ActivationForm = ({ code, tenantId }: ActivationFormProps) => {
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);
  const [requirementsMet, setRequirementsMet] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

  const form = useForm({
    defaultValues: activationFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(activationFormSchema),
  });

  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig(tenantId);
  const googleSiteKey =
    oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type: oidcUiConfig?.captcha?.provider === "hcaptcha" ? "hCaptcha" : "reCaptcha-v2-checkbox",
    generator: oidcUiConfig?.captcha?.generator,
  });
  const { isPending, mutateAsync } = useAccountActivation();

  useEffect(() => {
    if (!requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, requirementsMet, resetCaptcha]);

  useEffect(() => {
    if (!code) return navigate("/login");
  }, [code, navigate]);

  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");
  const { isValid } = form.formState;

  /* Inject PasswordStrengthChecker into the right panel's idle slot */
  const setPanelIdleSlot = animCtx?.setPanelIdleSlot;
  useEffect(() => {
    setPanelIdleSlot?.(
      <PasswordStrengthChecker
        password={password}
        confirmPassword={confirmPassword}
        onRequirementsMet={setRequirementsMet}
      />,
    );
    return () => {
      setPanelIdleSlot?.(null);
    };
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

  const onSubmitHandler = async (values: z.infer<typeof activationFormSchema>) => {
    animCtx?.startAnimation();
    try {
      const res = await mutateAsync({
        code: code,
        preventPostEvent: true,
        firstName: values.firstName,
        lastName: values.lastName,
        password: values.password,
        captchaCode,
        tenantId,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
            ? (Object.values(res.errors as Record<string, string>)[0] ?? "Activation failed")
            : (res.errors as string) || "Activation failed";
        shake();
        await animCtx?.failAnimation(msg);
        return;
      }
      await animCtx?.succeedAnimation();
      navigate("/activate-success");
    } catch (error: unknown) {
      resetCaptcha();
      shake();
      if (isErrorWithErrors(error)) {
        const msg = Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : error.errors && typeof error.errors === "object"
            ? (Object.values(error.errors as Record<string, string>)[0] ?? "Something went wrong")
            : (error.errors as unknown as string) || "Something went wrong";
        await animCtx?.failAnimation(msg);
      } else {
        await animCtx?.failAnimation("Something went wrong");
      }
    }
  };

  return (
    <form
      ref={formRef}
      onSubmit={form.handleSubmit(onSubmitHandler, shake)}
      onInput={() => {
        if (animCtx?.phase === "failed") animCtx?.resetAnimation();
      }}
      className="flex flex-col gap-5"
    >
      {/* First Name & Last Name */}
      <div className="flex flex-col sm:flex-row gap-4">
        <div className="flex flex-col gap-2 flex-1">
          <label htmlFor="activation-first-name" className="oidc-sci-fi-label">
            First Name
          </label>
          <input
            id="activation-first-name"
            type="text"
            autoComplete="given-name"
            placeholder="Jane"
            className="oidc-sci-fi-input"
            aria-invalid={!!form.formState.errors.firstName}
            disabled={isAuthenticating}
            {...form.register("firstName")}
          />
          {form.formState.errors.firstName && (
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              {form.formState.errors.firstName.message}
            </p>
          )}
        </div>
        <div className="flex flex-col gap-2 flex-1">
          <label htmlFor="activation-last-name" className="oidc-sci-fi-label">
            Last Name
          </label>
          <input
            id="activation-last-name"
            type="text"
            autoComplete="family-name"
            placeholder="Doe"
            className="oidc-sci-fi-input"
            aria-invalid={!!form.formState.errors.lastName}
            disabled={isAuthenticating}
            {...form.register("lastName")}
          />
          {form.formState.errors.lastName && (
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              {form.formState.errors.lastName.message}
            </p>
          )}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">Password</label>
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
            onClick={() => setShowPassword((v) => !v)}
            className="absolute right-3 top-1/2 -translate-y-1/2"
            style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
            aria-label={showPassword ? "Hide password" : "Show password"}
          >
            {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
          </button>
        </div>
        {form.formState.errors.password && (
          <p className="text-xs" style={{ color: "var(--danger)" }}>
            {form.formState.errors.password.message}
          </p>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">Confirm Password</label>
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
            onClick={() => setShowConfirmPassword((v) => !v)}
            className="absolute right-3 top-1/2 -translate-y-1/2"
            style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
            aria-label={showConfirmPassword ? "Hide password" : "Show password"}
          >
            {showConfirmPassword ? <EyeOff size={16} /> : <Eye size={16} />}
          </button>
        </div>
        {form.formState.errors.confirmPassword && (
          <p className="text-xs" style={{ color: "var(--danger)" }}>
            {form.formState.errors.confirmPassword.message}
          </p>
        )}
      </div>

      {captchaEnabled && isValid && requirementsMet && <Captcha {...captcha} />}

      <button
        type="submit"
        disabled={
          isAuthenticating ||
          (captchaEnabled && !captchaCode) ||
          !requirementsMet ||
          !isValid
        }
        className="oidc-sci-fi-btn mt-1 w-full flex items-center justify-center gap-2"
      >
        {isAuthenticating ? (
          <>
            <Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} />
            <span>Activating…</span>
          </>
        ) : (
          <>
            <span>Activate</span>
            <ArrowRight size={16} />
          </>
        )}
      </button>
    </form>
  );
};