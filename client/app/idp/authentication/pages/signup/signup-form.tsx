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
import { SIGNUP_ORGANIZATION_CHOICE_COPY } from "@blocks-idp/authentication/models/oidc-ui-template";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "react-router";
import React, { useEffect, useRef, useState } from "react";
import { useForm, useWatch } from "react-hook-form";
import { z } from "zod";
import { ArrowRight, Loader } from "lucide-react";
import { SsoSignin } from "../login/sso-signin";
import {
  buildSignupFormSchema,
  signupFormDefaultValue,
  signupFormSchema,
  type SignupOrganizationChoice,
} from "./utils";
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

// The project's auto-join organization is deleted or disabled.
const ORGANIZATION_UNAVAILABLE_KEY = "signup_organization_unavailable";

const hasErrorKey = (errors: unknown, key: string): boolean =>
  !!errors &&
  typeof errors === "object" &&
  !Array.isArray(errors) &&
  Object.keys(errors as Record<string, unknown>).some((k) => k.toLowerCase() === key);

export const SignupForm = ({
  loginOption,
  emailSignUpEnabled,
  ssoSignUpEnabled,
  tenantId,
  collectOrganizationName = false,
  signupAutoJoinEnabled = false,
}: {
  loginOption?: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
  tenantId?: string;
  /** The tenant allows creating an organization from sign-up. */
  collectOrganizationName?: boolean;
  /** New sign-ups join the project's organization. */
  signupAutoJoinEnabled?: boolean;
}) => {
  const [isChecked, setIsChecked] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  // Set when an unavailable auto-join organization switched the choice to "create"; the effect
  // below focuses the revealed name field once it is rendered and enabled again.
  const focusOrganizationNameRef = useRef(false);
  // Join-or-create: offered only when the user may both join the project's organization and
  // create their own. With creation off they simply join; with auto-join off they create (or not).
  const offerOrganizationChoice = collectOrganizationName && signupAutoJoinEnabled;
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);

  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(buildSignupFormSchema(collectOrganizationName, offerOrganizationChoice)),
  });
  const organizationChoice = useWatch({ control: form.control, name: "organizationChoice" });
  const wantsCreateOrganization =
    collectOrganizationName && (!offerOrganizationChoice || organizationChoice === "create");
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

  // Surfaces org failures on the organization input; returns true when handled
  // so the caller can skip the generic banner.
  const applyOrganizationError = (errors: unknown): boolean => {
    if (!wantsCreateOrganization || !errors || typeof errors !== "object" || Array.isArray(errors)) {
      return false;
    }
    const entry = Object.entries(errors as Record<string, string>).find(([key]) =>
      ORG_ERROR_KEYS.includes(key.toLowerCase()),
    );
    if (!entry) return false;
    form.setError("organizationName", { type: "server", message: entry[1] });
    return true;
  };

  const selectOrganizationChoice = (choice: SignupOrganizationChoice) => {
    if (choice === "create") {
      // The revealed name field re-validates the form as it mounts. Validating here as well would
      // race the user's first keystrokes and could leave a stale "invalid" behind.
      form.setValue("organizationChoice", choice);
      return;
    }
    // Joining needs no name: drop whatever was typed and any error it carried, then re-validate
    // so a submit blocked by the empty name is enabled again.
    form.setValue("organizationName", "");
    form.clearErrors("organizationName");
    form.setValue("organizationChoice", choice, { shouldValidate: true });
  };

  // The server error for the error area. An unavailable auto-join organization gets the built-in
  // sentence and, where the user may create their own, switches the choice to "create".
  const surfaceServerError = (errors: unknown, fallback: string): string => {
    if (hasErrorKey(errors, ORGANIZATION_UNAVAILABLE_KEY)) {
      setServerError(SIGNUP_ORGANIZATION_CHOICE_COPY.unavailableMessage);
      if (offerOrganizationChoice) {
        focusOrganizationNameRef.current = true;
        selectOrganizationChoice("create");
      }
      return SIGNUP_ORGANIZATION_CHOICE_COPY.unavailableMessage;
    }
    const handledOnField = applyOrganizationError(errors);
    if (!handledOnField) setServerError(fallback);
    return fallback;
  };

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    setServerError(null);
    animCtx?.startAnimation();
    try {
      // The activation email is built server-side, long after this page is gone, so the
      // originating application has to be named here for the user to get back to it.
      const { clientId, redirectUri } = extractOIDCParams();
      const res = await mutateAsync({
        firstName: values.firstName,
        lastName: values.lastName,
        email: values.email,
        captchaCode,
        tenantId,
        clientId,
        redirectUri,
        // "Join" passes false here; auth.service drops the flag and the name from the request
        // unless both are set, so the server places the user itself.
        createOrganizationDuringSignup: wantsCreateOrganization,
        organizationName: wantsCreateOrganization ? values.organizationName : undefined,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Registration failed")
          : (res.errors as string) || "Registration failed";
        const shown = surfaceServerError(res.errors, msg);
        shake();
        await animCtx?.failAnimation(shown);
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
        const shown = surfaceServerError(error.errors, msg);
        await animCtx?.failAnimation(shown);
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

  // Runs once the revealed name field is rendered and enabled again after the failed submit.
  useEffect(() => {
    if (!focusOrganizationNameRef.current || !wantsCreateOrganization || isAuthenticating) return;
    focusOrganizationNameRef.current = false;
    form.setFocus("organizationName");
  }, [wantsCreateOrganization, isAuthenticating, form]);

  if (!oidcUiConfig?.template) return null;
  const signupCopy = oidcUiConfig.template.pages.signup;

  const showSocialLogin = ssoSignUpEnabled && !!loginOption?.ssoInfo?.length;

  return (
    <div className="flex flex-col gap-4 w-full">
      {emailSignUpEnabled && (
        <Form {...form}>
          <form
            ref={formRef}
            onSubmit={(event) => form.handleSubmit(onSubmitHandler, shake)(event)}
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

            {/* Join-or-create — only when the tenant both auto-joins sign-ups and allows creation */}
            {offerOrganizationChoice && (
              <fieldset className="m-0 min-w-0 border-0 p-0">
                <legend className="oidc-sci-fi-label mb-2">{SIGNUP_ORGANIZATION_CHOICE_COPY.legend}</legend>
                <div className="flex flex-col gap-2">
                  {(
                    [
                      ["join", SIGNUP_ORGANIZATION_CHOICE_COPY.joinOption],
                      ["create", SIGNUP_ORGANIZATION_CHOICE_COPY.createOption],
                    ] as const
                  ).map(([choice, label]) => (
                    <label
                      key={choice}
                      htmlFor={`signup-organization-choice-${choice}`}
                      className="flex items-center gap-3 text-xs oidc-font-rajdhani"
                      style={{ color: "var(--fg)", lineHeight: 1.5, cursor: "pointer" }}
                    >
                      <input
                        id={`signup-organization-choice-${choice}`}
                        type="radio"
                        name="signup-organization-choice"
                        value={choice}
                        checked={organizationChoice === choice}
                        onChange={() => selectOrganizationChoice(choice)}
                        disabled={isAuthenticating}
                        style={{
                          accentColor: "var(--accent)",
                          width: "14px",
                          height: "14px",
                          flexShrink: 0,
                          cursor: "pointer",
                        }}
                      />
                      {label}
                    </label>
                  ))}
                </div>
              </fieldset>
            )}

            {/* Organization name — when the user is creating an organization at signup */}
            {wantsCreateOrganization && (
              <FormField
                control={form.control}
                name="organizationName"
                render={({ field }) => (
                  <FormItem>
                    <div className="flex flex-col gap-2">
                      <label htmlFor="signup-organization-name" className="oidc-sci-fi-label">
                        {signupCopy.organizationNameLabel}
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
                <Captcha {...captcha} />
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
                {signupCopy.termsConjunction}{" "}
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
              <p role="alert" className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>
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
                  <span>{signupCopy.creatingButton}</span>
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
          <span className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>{signupCopy.ssoSeparatorText}</span>
          <div className="flex-1 border-t" style={{ borderColor: "var(--border)" }} />
        </div>
      )}

      {showSocialLogin && loginOption && (
        <SsoSignin loginOption={loginOption} />
      )}
    </div>
  );
};
