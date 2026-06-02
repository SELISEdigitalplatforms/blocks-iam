import { getRuntimeEnv } from "@/lib/runtime-env";
import { Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { forgotPasswordFormSchema, forgotPasswordFormDefaultValue } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router-dom";
import { Captcha } from "@/components/captcha";
import { useEffect, useState } from "react";
import { useAccountRecover } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { ArrowRight, Loader } from "lucide-react";

export const ForgotPasswordForm = () => {
  const navigate = useNavigate();
  const [serverError, setServerError] = useState<string | null>(null);

  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(forgotPasswordFormSchema),
  });
  const { isPending, mutateAsync } = useAccountRecover();
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const { captcha, code: captchaCode, reset: resetCaptcha } = useCaptcha({
    siteKey: googleSiteKey,
    type: "reCaptcha-v2-checkbox",
  });
  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<typeof forgotPasswordFormSchema>) => {
    setServerError(null);
    try {
      const res = await mutateAsync({ ...values, captchaCode });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Something went wrong")
          : (res.errors as string) || "Something went wrong";
        setServerError(msg);
        return;
      }
      navigate(`/forgot-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) {
        const msg = Array.isArray(error.errors)
          ? (error.errors[0] as string)
          : error.errors && typeof error.errors === "object"
          ? (Object.values(error.errors as Record<string, string>)[0] ?? "Something went wrong")
          : (error.errors as unknown as string) || "Something went wrong";
        setServerError(msg);
      } else {
        setServerError("Something went wrong");
      }
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="text-xl font-semibold mb-2" style={{ color: "var(--fg)", fontFamily: "system-ui, -apple-system, sans-serif" }}>
          Reset Password
        </h2>
        <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, -apple-system, sans-serif" }}>
          Enter your email and we&apos;ll dispatch a recovery link.
        </p>
      </div>

      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <label className="oidc-sci-fi-label">Email</label>
          <input
            type="email"
            placeholder="name@company.com"
            autoComplete="email"
            className="oidc-sci-fi-input"
            aria-invalid={!!form.formState.errors.email}
            {...form.register("email")}
          />
          {form.formState.errors.email && (
            <p className="text-xs" style={{ color: "var(--danger)" }}>{form.formState.errors.email.message}</p>
          )}
        </div>

        {isValid && <Captcha {...captcha} />}

        {serverError && (
          <p className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>{serverError}</p>
        )}

        <button
          type="submit"
          disabled={isPending || !isValid || !captchaCode}
          className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
        >
          {isPending ? (
            <><Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} /><span>Sending...</span></>
          ) : (
            <><span>Send Recovery Link</span><ArrowRight size={16} /></>
          )}
        </button>
      </form>

      <Link to="/login" className="oidc-sci-fi-link text-sm text-center">
        Back to sign in
      </Link>
    </div>
  );
};
