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
import { useEffect, useRef, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { ArrowRight, Loader } from "lucide-react";
import { SsoSignin } from "../login/sso-signin";
import { signupFormDefaultValue, signupFormSchema } from "./utils";

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
  const [isSubmitting, setIsSubmitting] = useState(false);
  const navigate = useNavigate();
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
  const isAuthenticating = isPending || isSubmitting;

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("su-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("su-shake");
  }

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    setIsSubmitting(true);
    try {
      const res = await mutateAsync({ ...values, captchaCode });
      if (!res.isSuccess) {
        resetCaptcha();
        shake();
        showErrorToast({ errors: res.errors });
        return;
      }
      navigate(`/signup-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      shake();
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  const showSocialLogin = ssoSignUpEnabled && !!loginOption?.ssoInfo?.length;

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "16px", width: "100%" }}>
      <style>{`
        @keyframes su-shake-kf {
          0%,100%{transform:translateX(0)}
          20%{transform:translateX(-8px)}
          40%{transform:translateX(8px)}
          60%{transform:translateX(-4px)}
          80%{transform:translateX(4px)}
        }
        .su-shake { animation: su-shake-kf 0.4s ease; }
      `}</style>

      {emailSignUpEnabled && (
        <Form {...form}>
          <form
            ref={formRef}
            onSubmit={form.handleSubmit(onSubmitHandler, shake)}
            style={{ display: "flex", flexDirection: "column", gap: "18px" }}
            noValidate
          >
            {/* Email */}
            <FormField
              control={form.control}
              name="email"
              render={({ field }) => (
                <FormItem>
                  <div style={{ display: "flex", flexDirection: "column", gap: "6px" }}>
                    <label htmlFor="signup-email" className="su-label">Work Email</label>
                    <FormControl>
                      <input
                        id="signup-email"
                        type="email"
                        autoComplete="email"
                        placeholder="name@company.com"
                        className="su-input"
                        aria-invalid={!!form.formState.errors.email}
                        disabled={isAuthenticating}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage className="su-error" />
                  </div>
                </FormItem>
              )}
            />

            {/* CAPTCHA */}
            {isValid && (
              <div>
                <Captcha {...(captcha as any)} />
              </div>
            )}

            {/* Terms */}
            <div style={{ display: "flex", alignItems: "flex-start", gap: "10px" }}>
              <input
                id="signup-terms"
                type="checkbox"
                checked={isChecked}
                onChange={(e) => setIsChecked(e.target.checked)}
                disabled={isAuthenticating}
                style={{ marginTop: "2px", accentColor: "var(--su-accent)", width: "14px", height: "14px", flexShrink: 0, cursor: "pointer" }}
              />
              <label
                htmlFor="signup-terms"
                style={{ fontSize: "0.75rem", color: "var(--su-muted)", lineHeight: 1.5, cursor: "pointer", fontFamily: "system-ui, sans-serif" }}
              >
                I agree to the{" "}
                <Link to="https://selisegroup.com/software-development-terms/" className="su-link" target="_blank" rel="noopener noreferrer">
                  Terms of Service
                </Link>{" "}
                and the{" "}
                <Link to="https://selisegroup.com/privacy-policy/" className="su-link" target="_blank" rel="noopener noreferrer">
                  Privacy Policy
                </Link>
                .
              </label>
            </div>

            {/* Submit */}
            <button
              type="submit"
              disabled={isAuthenticating || !isValid || !captchaCode || !isChecked}
              className="su-btn"
            >
              {isAuthenticating ? (
                <>
                  <Loader size={15} style={{ animation: "su-spin 1s linear infinite" }} />
                  <span>Creating Account…</span>
                </>
              ) : (
                <>
                  <span>Create Account</span>
                  <ArrowRight size={15} />
                </>
              )}
            </button>
          </form>
        </Form>
      )}

      {emailSignUpEnabled && showSocialLogin && (
        <div style={{ display: "flex", alignItems: "center", gap: "12px", margin: "4px 0" }}>
          <div style={{ flex: 1, borderTop: "1px solid var(--su-border)" }} />
          <span style={{ fontSize: "0.72rem", color: "var(--su-muted)", fontFamily: "system-ui, sans-serif" }}>or</span>
          <div style={{ flex: 1, borderTop: "1px solid var(--su-border)" }} />
        </div>
      )}

      {showSocialLogin && <SsoSignin loginOption={loginOption} />}
    </div>
  );
};
