import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { activationFormDefaultValue, activationFormSchema } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Captcha } from "@/components/captcha";
import { useNavigate } from "react-router";
import { useEffect, useRef, useState } from "react";
import { useAccountActivation } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { ArrowRight, Eye, EyeOff, Loader } from "lucide-react";
import { useOidcAuthAnimation } from "../oidc/oidc-auth-shell";
import { appendTenantId, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

type ActivationFormProps = {
  code: string;
  tenantId?: string;
  /** Prefilled from the account when it already has them — see validate-activation. */
  firstName?: string;
  lastName?: string;
};

export const ActivationForm = ({
  code,
  tenantId,
  firstName,
  lastName,
}: ActivationFormProps) => {
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);
  const [requirementsMet, setRequirementsMet] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

  const form = useForm({
    // The parent only renders this form once validate-activation has resolved, so these
    // are already settled on first render — no reset needed. They stay editable, and
    // whatever is submitted here wins over what the account currently holds.
    defaultValues: {
      ...activationFormDefaultValue,
      firstname: firstName || activationFormDefaultValue.firstname,
      lastname: lastName || activationFormDefaultValue.lastname,
    },
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(activationFormSchema),
  });

  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig(tenantId);
  const activationCopy = (oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE).pages.activation;
  const googleSiteKey =
    oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type:
      oidcUiConfig?.captcha?.provider === "hcaptcha"
        ? "hCaptcha"
        : "reCaptcha-v2-checkbox",
    generator: oidcUiConfig?.captcha?.generator,
  });
  const { isPending, mutateAsync } = useAccountActivation();

  useEffect(() => {
    if (!requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, requirementsMet, resetCaptcha]);

  useEffect(() => {
    if (!code) {
      navigate("/login");
      return;
    }
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

  const onSubmitHandler = async (
    values: z.infer<typeof activationFormSchema>,
  ) => {
    animCtx?.startAnimation();
    try {
      const res = await mutateAsync({
        code: code,
        preventPostEvent: true,
        firstName: values.firstname,
        lastName: values.lastname,
        password: values.password,
        captchaCode,
        tenantId,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
            ? (Object.values(res.errors as Record<string, string>)[0] ??
              "Activation failed")
            : (res.errors as string) || "Activation failed";
        shake();
        await animCtx?.failAnimation(msg);
        return;
      }
      await animCtx?.succeedAnimation();
      // The activation link carries the originating app's clientId/redirect_uri; keep
      // them so the success page's "Log in" re-enters that OIDC flow. tenantId is a
      // path segment here, which buildOIDCNavigationUrl cannot see, so add it by hand.
      navigate(appendTenantId(buildOIDCNavigationUrl("/oidc/activate-success"), tenantId));
    } catch (error: unknown) {
      resetCaptcha();
      shake();
      if (isErrorWithErrors(error)) {
        const msg = Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : error.errors && typeof error.errors === "object"
            ? (Object.values(error.errors as Record<string, string>)[0] ??
              "Something went wrong")
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
      className="flex flex-col gap-5">
      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">First Name</label>
        <input
          type="text"
          placeholder="First name"
          autoComplete="given-name"
          className="oidc-sci-fi-input"
          aria-invalid={!!form.formState.errors.firstname}
          disabled={isAuthenticating}
          {...form.register("firstname")}
        />
        {form.formState.errors.firstname && (
          <p className="text-xs" style={{ color: "var(--danger)" }}>
            {form.formState.errors.firstname.message}
          </p>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">Last Name</label>
        <input
          type="text"
          placeholder="Last name"
          autoComplete="family-name"
          className="oidc-sci-fi-input"
          aria-invalid={!!form.formState.errors.lastname}
          disabled={isAuthenticating}
          {...form.register("lastname")}
        />
        {form.formState.errors.lastname && (
          <p className="text-xs" style={{ color: "var(--danger)" }}>
            {form.formState.errors.lastname.message}
          </p>
        )}
      </div>

      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">{activationCopy.passwordLabel}</label>
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
            style={{
              color: "var(--muted)",
              background: "none",
              border: "none",
              cursor: "pointer",
              padding: 0,
            }}
            aria-label={showPassword ? "Hide password" : "Show password"}>
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
        <label className="oidc-sci-fi-label">{activationCopy.confirmPasswordLabel}</label>
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
            style={{
              color: "var(--muted)",
              background: "none",
              border: "none",
              cursor: "pointer",
              padding: 0,
            }}
            aria-label={
              showConfirmPassword ? "Hide password" : "Show password"
            }>
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
        className="oidc-sci-fi-btn mt-1 w-full flex items-center justify-center gap-2">
        {isAuthenticating ? (
          <>
            <Loader
              size={16}
              style={{ animation: "oidc-spin 1s linear infinite" }}
            />
            <span>Activating…</span>
          </>
        ) : (
          <>
            <span>{activationCopy.submitButton}</span>
            <ArrowRight size={16} />
          </>
        )}
      </button>
    </form>
  );
};
