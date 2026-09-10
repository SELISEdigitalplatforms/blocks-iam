import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { activationFormDefaultValue, activationFormSchema } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Captcha } from "@/components/captcha";
import { useNavigate } from "react-router";
import { useCallback, useEffect, useRef, useState } from "react";
import {
  useAccountActivation,
  useAccountActivationCodeExpiration,
} from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { ArrowRight, Eye, EyeOff, Loader } from "lucide-react";
import { useOidcAuthAnimation } from "../oidc/oidc-auth-shell";
import { appendTenantId, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { resolveActivationCodeStatus } from "./activation-status";

/** Pulls a readable message out of the several shapes an activation failure arrives in. */
const readErrorMessage = (source: unknown, fallback: string): string => {
  if (Array.isArray(source)) return (source[0] as string) ?? fallback;
  if (source && typeof source === "object") {
    return Object.values(source as Record<string, string>)[0] ?? fallback;
  }
  return (source as string) || fallback;
};

type ActivationFormProps = {
  code: string;
  tenantId?: string;
  /** Prefilled from the account when it already has them — see validate-activation. */
  firstName?: string;
  lastName?: string;
  /**
   * Whether to ask for a password. Resolved by the parent from the tenant's IAM
   * configuration; when false, confirming this form is all the activation needs.
   */
  collectPassword?: boolean;
};

export const ActivationForm = ({
  code,
  tenantId,
  firstName,
  lastName,
  collectPassword = true,
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
  const { mutateAsync: revalidateCode } = useAccountActivationCodeExpiration();
  const [autoActivationError, setAutoActivationError] = useState<string | null>(null);

  const successUrl = appendTenantId(
    buildOIDCNavigationUrl("/oidc/activate-success"),
    tenantId,
  );

  /**
   * A failed activation is worth a second look before it is shown as one: a link scanner, another
   * tab or a retried request may have already finished the job, in which case the account is
   * active and the code is spent -- which is what the failure actually was.
   */
  const settleFailure = useCallback(
    async (message: string) => {
      // Only the automatic path renders this; the form shows its failure through the animation.
      const report = (text: string) => {
        if (!collectPassword) setAutoActivationError(text);
      };

      try {
        const check = await revalidateCode({ activationCode: code, tenantId });
        if (resolveActivationCodeStatus(check) === "AlreadyActivated") {
          // Nothing was lost when no password was being collected, so the success page is the
          // truthful end. With a password in hand it was not applied, and saying so beats
          // implying it was.
          if (!collectPassword) {
            await animCtx?.succeedAnimation();
            navigate(successUrl, { replace: true });
            return;
          }
          const alreadyActive = "This account is already active. Please sign in to continue.";
          report(alreadyActive);
          await animCtx?.failAnimation(alreadyActive);
          return;
        }
      } catch {
        // The re-check is a courtesy; its own failure must not replace the real one.
      }

      report(message);
      await animCtx?.failAnimation(message);
    },
    [revalidateCode, code, tenantId, collectPassword, animCtx, navigate, successUrl],
  );

  // Without a password there are no strength requirements to meet, and this guard would
  // otherwise throw away every solved captcha the moment it arrives.
  useEffect(() => {
    if (collectPassword && !requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, collectPassword, requirementsMet, resetCaptcha]);

  useEffect(() => {
    if (!code) {
      navigate("/login");
      return;
    }
  }, [code, navigate]);

  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");
  const { isValid } = form.formState;

  /** Nothing to satisfy when the password step is off. */
  const passwordRequirementsMet = !collectPassword || requirementsMet;
  const canSubmit = isValid && passwordRequirementsMet;

  /* Inject PasswordStrengthChecker into the right panel's idle slot */
  const setPanelIdleSlot = animCtx?.setPanelIdleSlot;
  useEffect(() => {
    if (!collectPassword) {
      setPanelIdleSlot?.(null);
      return;
    }

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
  }, [password, confirmPassword, setPanelIdleSlot, collectPassword]);

  if (!oidcUiConfig?.template) return null;
  const activationCopy = oidcUiConfig.template.pages.activation;

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

  /**
   * The activation request itself, shared by the form and by the automatic path. `onFailure`
   * differs between them: the form has something to shake and a captcha to reset, the automatic
   * path has neither.
   */
  const runActivation = useCallback(
    async (
      values: { firstname?: string; lastname?: string; password?: string },
      onFailure?: () => void,
    ) => {
      animCtx?.startAnimation();
      setAutoActivationError(null);
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
          onFailure?.();
          await settleFailure(readErrorMessage(res.errors, "Activation failed"));
          return;
        }
        await animCtx?.succeedAnimation();
        // The activation link carries the originating app's clientId/redirect_uri; keep
        // them so the success page's "Log in" re-enters that OIDC flow. tenantId is a
        // path segment here, which buildOIDCNavigationUrl cannot see, so add it by hand.
        navigate(successUrl);
      } catch (error: unknown) {
        onFailure?.();
        await settleFailure(
          isErrorWithErrors(error)
            ? readErrorMessage(error.errors, "Something went wrong")
            : "Something went wrong",
        );
      }
    },
    [animCtx, mutateAsync, code, captchaCode, tenantId, settleFailure, navigate, successUrl],
  );

  /**
   * With the password step off there is nothing to fill in, so confirming the emailed link is the
   * whole flow: activate on arrival and hand the user to the success page.
   *
   * The latch holds that to once per page load. The effect re-runs on any render -- StrictMode
   * double-invokes it in development, a configuration refetch or the captcha token arriving does
   * it in production -- and the first call spends the code, so a second would report a failure
   * over a success.
   */
  const autoActivateStarted = useRef(false);
  useEffect(() => {
    if (collectPassword || !code) return;
    // Captcha stays a real gate: wait for the widget's token, then go without asking for a
    // second gesture. There is no invisible provider wired up, so no token means no check ran.
    if (captchaEnabled && !captchaCode) return;
    if (autoActivateStarted.current) return;
    autoActivateStarted.current = true;

    void runActivation({});
  }, [collectPassword, code, captchaEnabled, captchaCode, runActivation]);

  const onSubmitHandler = async (values: z.infer<typeof activationFormSchema>) =>
    runActivation(
      {
        firstname: values.firstname,
        lastname: values.lastname,
        password: collectPassword ? values.password : undefined,
      },
      () => {
        resetCaptcha();
        shake();
      },
    );

  // Nothing is asked for when the password step is off, so there is no form to render: either the
  // captcha is waiting for its one gesture, or the activation is already on its way.
  if (!collectPassword) {
    const waitingForCaptcha = captchaEnabled && !captchaCode;

    return (
      <div className="flex flex-col gap-5">
        <p
          className="text-sm"
          style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}
        >
          {autoActivationError
            ? autoActivationError
            : waitingForCaptcha
              ? activationCopy.autoConfirmCaptchaText
              : activationCopy.autoConfirmProgressText}
        </p>

        {waitingForCaptcha && !autoActivationError && <Captcha {...captcha} />}

        {!waitingForCaptcha && !autoActivationError && (
          <div
            className="flex items-center justify-center gap-2 py-2 text-sm"
            style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}
            role="status"
          >
            <Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} />
            <span>{activationCopy.autoActivatingLabel}</span>
          </div>
        )}

        {autoActivationError && (
          <LoginReturnLink className="oidc-sci-fi-btn inline-block px-5 py-2.5 text-center no-underline">
            {activationCopy.backToLoginButton}
          </LoginReturnLink>
        )}
      </div>
    );
  }

  return (
    <form
      ref={formRef}
      onSubmit={form.handleSubmit(onSubmitHandler, shake)}
      onInput={() => {
        if (animCtx?.phase === "failed") animCtx?.resetAnimation();
      }}
      className="flex flex-col gap-5"
    >
      <div className="flex flex-col gap-2">
        <label className="oidc-sci-fi-label">{activationCopy.firstNameLabel}</label>
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
        <label className="oidc-sci-fi-label">{activationCopy.lastNameLabel}</label>
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

      {collectPassword && (
        <>
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
        </>
      )}

      {captchaEnabled && canSubmit && <Captcha {...captcha} />}

      <button
        type="submit"
        disabled={isAuthenticating || (captchaEnabled && !captchaCode) || !canSubmit}
        className="oidc-sci-fi-btn mt-1 w-full flex items-center justify-center gap-2"
      >
        {isAuthenticating ? (
          <>
            <Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} />
            <span>{activationCopy.activatingButton}</span>
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
