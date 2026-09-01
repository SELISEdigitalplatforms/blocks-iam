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
import { DEFAULT_OIDC_UI_TEMPLATE } from "@blocks-idp/authentication/models/oidc-ui-template";
import { useSignupByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { LoginOption } from "@blocks-idp/authentication/models/auth.model";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "react-router";
import React, { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { ArrowRight, Loader } from "lucide-react";
import { SsoSignin } from "../login/sso-signin";
import { buildSignupFormSchema, signupFormDefaultValue, signupFormSchema } from "./utils";
import { useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { buildOIDCNavigationUrl, extractOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

// Server-side org failures that belong on the organization field rather than in
// the generic error banner.
const ORG_ERROR_KEYS = [
  "organizationname",
  "name_already_exists",
  "org_creation_disabled",
  "multi_org_disabled",
  "organization_creation_failed",
];

export const SignupForm = ({
  loginOption,
  emailSignUpEnabled,
  ssoSignUpEnabled,
  tenantId,
  collectOrganizationName = false,
}: {
  loginOption?: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
  tenantId?: string;
  collectOrganizationName?: boolean;
}) => {
  const [isChecked, setIsChecked] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);

  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(buildSignupFormSchema(collectOrganizationName)),
  });
  const { isPending, mutateAsync } = useSignupByEmail();
  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig(tenantId);
  const signupCopy = (oidcUiConfig?.template ?? DEFAULT_OIDC_UI_TEMPLATE).pages.signup;

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

  // Surfaces org failures on the organization input; returns true when handled
  // so the caller can skip the generic banner.
  const applyOrganizationError = (errors: unknown): boolean => {
    if (!collectOrganizationName || !errors || typeof errors !== "object" || Array.isArray(errors)) {
      return false;
    }
    const entry = Object.entries(errors as Record<string, string>).find(([key]) =>
      ORG_ERROR_KEYS.includes(key.toLowerCase()),
    );
    if (!entry) return false;
    form.setError("organizationName", { type: "server", message: entry[1] });
    return true;
  };

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    setServerError(null);
    animCtx?.startAnimation();
    try {
      // The activation email is built server-side, long after this page is gone, so the
      // originating application has to be named here for the user to get back to it.
      const { clientId, redirectUri } = extractOIDCParams();
      const res = await mutateAsync({
        ...values,
        captchaCode,
        tenantId,
        clientId,
        redirectUri,
        createOrganizationDuringSignup: collectOrganizationName,
        organizationName: collectOrganizationName ? values.organizationName : undefined,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Registration failed")
          : (res.errors as string) || "Registration failed";
        const handledOnField = applyOrganizationError(res.errors);
        if (!handledOnField) setServerError(msg);
        shake();
        await animCtx?.failAnimation(msg);
        return;
      }
      await animCtx?.succeedAnimation();
      // Stay inside the OIDC flow: the confirmation page needs clientId, state,
      // redirect_uri and friends so "Go to login" can hand the user back to the
      // application they started from instead of stranding them on the IAM login.
      const emailSentUrl = buildOIDCNavigationUrl("/oidc/signup-email-sent");
      const separator = emailSentUrl.includes("?") ? "&" : "?";
      navigate(`${emailSentUrl}${separator}email=${encodeURIComponent(values.email)}`);
    } catch (error) {
      resetCaptcha();
      shake();
      if (isErrorWithErrors(error)) {
        const msg = Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : error.errors && typeof error.errors === "object"
          ? (Object.values(error.errors as Record<string, string>)[0] ?? "Something went wrong")
          : (error.errors as unknown as string) || "Something went wrong";
        const handledOnField = applyOrganizationError(error.errors);
        if (!handledOnField) setServerError(msg);
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
                        {signupCopy.firstNameLabel}
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
                        {signupCopy.lastNameLabel}
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
                      {signupCopy.emailLabel}
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

            {/* Organization name — only when the tenant allows org creation from signup */}
            {collectOrganizationName && (
              <FormField
                control={form.control}
                name="organizationName"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex flex-col gap-2">
                      <label htmlFor="signup-organization-name" className="oidc-sci-fi-label">
                        Organization Name
                      </label>
                      <FormControl>
                        <input
                          id="signup-organization-name"
                          type="text"
                          autoComplete="organization"
                          placeholder="Acme Inc."
                          className="oidc-sci-fi-input"
                          aria-invalid={!!form.formState.errors.organizationName}
                          disabled={isAuthenticating}
                          {...field}
                        />
                      </FormControl>
                      <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                    </div>
                  </FormItem>
                )}
              />
            )}

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
                {signupCopy.termsPrefix}{" "}
                <Link
                  to="https://selisegroup.com/software-development-terms/"
                  className="oidc-sci-fi-link"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {signupCopy.termsLinkText}
                </Link>{" "}
                and the{" "}
                <Link
                  to="https://selisegroup.com/privacy-policy/"
                  className="oidc-sci-fi-link"
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {signupCopy.privacyLinkText}
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
                  <span>{signupCopy.submitButton}</span>
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
