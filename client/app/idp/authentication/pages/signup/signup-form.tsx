import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Captcha } from "@/components/captcha";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useSignupByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { LoginOption } from "@blocks-idp/authentication/models/auth-configuration.model";
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
}: {
  loginOption: LoginOption;
  emailSignUpEnabled: boolean;
  ssoSignUpEnabled: boolean;
}) => {
  const [isChecked, setIsChecked] = useState(false);
  const navigate = useNavigate();
  const animCtx = useOidcAuthAnimation();
  const formRef = useRef<HTMLFormElement>(null);

  const form = useForm({
    defaultValues: signupFormDefaultValue,
    resolver: zodResolver(signupFormSchema),
  });
  const { isPending, mutateAsync } = useSignupByEmail();

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    code: captchaCode,
    captcha,
    reset: resetCaptcha,
  } = useCaptcha({
    type: "reCaptcha-v2-checkbox",
    siteKey: googleSiteKey,
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
    animCtx?.startAnimation();
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : (res.errors as string) || "Registration failed";
        shake();
        await animCtx?.failAnimation(msg);
        showErrorToast({ errors: res.errors });
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
          : (error.errors as unknown as string) || "Something went wrong";
        await animCtx?.failAnimation(msg);
        showErrorToast({ errors: error.errors });
      } else {
        await animCtx?.failAnimation("Something went wrong");
        showErrorToast({ errors: "Something went wrong" });
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
          <form ref={formRef} onSubmit={form.handleSubmit(onSubmitHandler, shake)} className="flex flex-col gap-5 w-full" noValidate>
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

            {/* CAPTCHA (shown when form is valid) */}
            {isValid && (
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

            {/* Submit */}
            <button
              type="submit"
              disabled={isAuthenticating || !isValid || !captchaCode || !isChecked}
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

      {showSocialLogin && (
        <SsoSignin loginOption={loginOption} />
      )}
    </div>
  );
};
