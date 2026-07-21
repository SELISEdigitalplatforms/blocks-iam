import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Captcha } from "@/components/captcha";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useSignupByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { LoginOption } from "@blocks-idp/authentication/models/auth.model";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "react-router-dom";
import React, { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { ArrowRight, Loader } from "lucide-react";
import { SsoSignin } from "../login/sso-signin";
import { signupFormDefaultValue, signupFormSchema } from "./utils";
import { useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";

export const SignupForm = ({
  loginOption,
  emailSignUpEnabled,
  ssoSignUpEnabled,
  tenantId,
}: {
  loginOption?: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
  tenantId?: string;
}) => {
  const [isChecked, setIsChecked] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);

  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(signupFormSchema),
  });
  const { isPending, mutateAsync } = useSignupByEmail();
  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig(tenantId);

  const googleSiteKey =
    oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const captchaType =
    oidcUiConfig?.captcha?.provider === "hcaptcha" ? "hCaptcha" : "reCaptcha-v2-checkbox";
  const {
    code: captchaCode,
    captcha,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type: captchaType,
    generator: oidcUiConfig?.captcha?.generator,
  });

  const { isValid } = form.formState;

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

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    setServerError(null);
    animCtx?.startAnimation();
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
        tenantId,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Registration failed")
          : (res.errors as string) || "Registration failed";
        setServerError(msg);
        shake();
        await animCtx?.failAnimation(msg);
        return;
      }
      await animCtx?.succeedAnimation();
      navigate(`/signup-email-sent?email=${values.email}`);
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

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  const showSocialLogin = ssoSignUpEnabled && !!loginOption?.ssoInfo?.length;

  return (
    <div className="flex flex-col gap-4 w-full">
      {emailSignUpEnabled && (
        <Form {...form}>
          <form
            ref={formRef}
            onSubmit={form.handleSubmit(onSubmitHandler, shake)}
            onInput={() => {
              if (serverError) setServerError(null);
              if (animCtx?.phase === "failed") animCtx?.resetAnimation();
            }}
            className="flex flex-col gap-5 w-full"
            noValidate
          >
            {/* First Name & Last Name */}
            <div className="flex flex-col sm:flex-row gap-4">
              <FormField
                control={form.control}
                name="firstName"
                render={({ field }) => (
                  <FormItem className="flex-1">
                    <div className="flex flex-col gap-2">
                      <label htmlFor="signup-first-name" className="oidc-sci-fi-label">
                        First Name
                      </label>
                      <FormControl>
                        <input
                          id="signup-first-name"
                          type="text"
                          autoComplete="given-name"
                          placeholder="Jane"
                          className="oidc-sci-fi-input"
                          aria-invalid={!!form.formState.errors.firstName}
                          disabled={isAuthenticating}
                          {...field}
                        />
                      </FormControl>
                      <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                    </div>
                  </FormItem>
                )}
              />
              <FormField
                control={form.control}
                name="lastName"
                render={({ field }) => (
                  <FormItem className="flex-1">
                    <div className="flex flex-col gap-2">
                      <label htmlFor="signup-last-name" className="oidc-sci-fi-label">
                        Last Name
                      </label>
                      <FormControl>
                        <input
                          id="signup-last-name"
                          type="text"
                          autoComplete="family-name"
                          placeholder="Doe"
                          className="oidc-sci-fi-input"
                          aria-invalid={!!form.formState.errors.lastName}
                          disabled={isAuthenticating}
                          {...field}
                        />
                      </FormControl>
                      <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                    </div>
                  </FormItem>
                )}
              />
            </div>

            {/* Email */}
            <FormField
              control={form.control}
              name="email"
              render={({ field }) => (
                <FormItem>
                  <div className="flex flex-col gap-2">
                    <label htmlFor="signup-email" className="oidc-sci-fi-label">
                      Work Email
                    </label>
                    <FormControl>
                      <input
                        id="signup-email"
                        type="email"
                        autoComplete="email"
                        placeholder="name@company.com"
                        className="oidc-sci-fi-input"
                        aria-invalid={!!form.formState.errors.email}
                        disabled={isAuthenticating}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                  </div>
                </FormItem>
              )}
            />

            {/* CAPTCHA (shown when captcha is enabled and form is valid) */}
            {captchaEnabled && isValid && (
              <div>
                <Captcha {...(captcha as any)} />
              </div>
            )}

            {/* Terms checkbox */}
            <div className="flex items-start gap-3">
              <input
                id="signup-terms"
                type="checkbox"
                checked={isChecked}
                onChange={(e) => setIsChecked(e.target.checked)}
                disabled={isAuthenticating}
                style={{
                  marginTop: "2px",
                  accentColor: "var(--accent)",
                  width: "14px",
                  height: "14px",
                  flexShrink: 0,
                  cursor: "pointer",
                }}
              />
              <label
                htmlFor="signup-terms"
                className="text-xs oidc-font-rajdhani"
                style={{ color: "var(--muted)", lineHeight: 1.5, cursor: "pointer" }}
              >
                I agree to the{" "}
                <Link
                  to="https://selisegroup.com/software-development-terms/"
                  className="oidc-sci-fi-link"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Terms of Service
                </Link>{" "}
                and the{" "}
                <Link
                  to="https://selisegroup.com/privacy-policy/"
                  className="oidc-sci-fi-link"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Privacy Policy
                </Link>
                .
              </label>
            </div>

            {serverError && (
              <p className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>
                {serverError}
              </p>
            )}

            {/* Submit */}
            <button
              type="submit"
              disabled={
                isAuthenticating ||
                !isValid ||
                (captchaEnabled && !captchaCode) ||
                !isChecked
              }
              className="oidc-sci-fi-btn mt-1 w-full flex items-center justify-center gap-2"
            >
              {isAuthenticating ? (
                <>
                  <Loader size={16} className="oidc-spin-slow" />
                  <span>Creating Account…</span>
                </>
              ) : (
                <>
                  <span>Create Account</span>
                  <ArrowRight size={16} />
                </>
              )}
            </button>
          </form>
        </Form>
      )}

      {emailSignUpEnabled && showSocialLogin && (
        <div className="my-2 mt-4 flex items-center gap-3">
          <div className="flex-1 border-t" style={{ borderColor: "var(--border)" }} />
          <span className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>or</span>
          <div className="flex-1 border-t" style={{ borderColor: "var(--border)" }} />
        </div>
      )}

      {showSocialLogin && loginOption && (
        <SsoSignin loginOption={loginOption} />
      )}
    </div>
  );
};
