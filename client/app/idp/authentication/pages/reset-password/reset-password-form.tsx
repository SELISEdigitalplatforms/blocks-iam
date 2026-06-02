import { getRuntimeEnv } from "@/lib/runtime-env";
import { Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import {
  resetPasswordFormSchema,
  ResetPasswordFormValuesType,
  resetPasswordFormDefaultValue,
} from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { useNavigate } from "react-router-dom";
import { Captcha } from "@/components/captcha";
import { useEffect, useState } from "react";
import { useAccountResetPassword } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { PasswordStrengthChecker } from "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker";
import { Switch } from "@/components/ui-kits/switch/switch";
import { ArrowRight, Loader } from "lucide-react";

type ResetPasswordFormProps = { code: string };

export const ResetPasswordForm = ({ code }: ResetPasswordFormProps) => {
  const navigate = useNavigate();
  const [serverError, setServerError] = useState<string | null>(null);
  const [requirementsMet, setRequirementsMet] = useState(false);

  const form = useForm<ResetPasswordFormValuesType>({
    defaultValues: resetPasswordFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(resetPasswordFormSchema),
  });

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const { captcha, code: captchaCode, reset: resetCaptcha } = useCaptcha({
    siteKey: googleSiteKey,
    type: "reCaptcha-v2-checkbox",
  });

  const { isPending, mutateAsync } = useAccountResetPassword();
  const { isValid } = form.formState;
  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");
  const logoutFromAllDevices = form.watch("logoutFromAllDevices");

  useEffect(() => {
    if (!isValid && !requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, requirementsMet, resetCaptcha]);

  const onSubmitHandler = async (values: ResetPasswordFormValuesType) => {
    setServerError(null);
    try {
      const res = await mutateAsync({
        code,
        captchaCode,
        logoutFromAllDevices: values.logoutFromAllDevices,
        password: values.password,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        const msg = Array.isArray(res.errors)
          ? res.errors[0]
          : res.errors && typeof res.errors === "object"
          ? (Object.values(res.errors as Record<string, string>)[0] ?? "Reset failed")
          : (res.errors as string) || "Reset failed";
        setServerError(msg);
        return;
      }
      navigate("/reset-password-success");
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

  return (
    <div className="flex flex-col gap-6">
      <div>
        <h2 className="text-xl font-semibold mb-2" style={{ color: "var(--fg)", fontFamily: "system-ui, -apple-system, sans-serif" }}>
          Set New Password
        </h2>
        <p className="text-sm" style={{ color: "var(--muted)", fontFamily: "system-ui, -apple-system, sans-serif" }}>
          Choose a strong password for your account.
        </p>
      </div>

      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-5">
        <div className="flex flex-col gap-2">
          <label className="oidc-sci-fi-label">New Password</label>
          <input
            type="password"
            placeholder="••••••••"
            autoComplete="new-password"
            className="oidc-sci-fi-input"
            aria-invalid={!!form.formState.errors.password}
            {...form.register("password")}
          />
          {form.formState.errors.password && (
            <p className="text-xs" style={{ color: "var(--danger)" }}>{form.formState.errors.password.message}</p>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <label className="oidc-sci-fi-label">Confirm Password</label>
          <input
            type="password"
            placeholder="••••••••"
            autoComplete="new-password"
            className="oidc-sci-fi-input"
            aria-invalid={!!form.formState.errors.confirmPassword}
            {...form.register("confirmPassword")}
          />
          {form.formState.errors.confirmPassword && (
            <p className="text-xs" style={{ color: "var(--danger)" }}>{form.formState.errors.confirmPassword.message}</p>
          )}
        </div>

        <PasswordStrengthChecker
          password={password}
          confirmPassword={confirmPassword}
          onRequirementsMet={setRequirementsMet}
        />

        <div
          className="rounded-lg p-4 flex items-center justify-between gap-3"
          style={{ border: "1px solid var(--border)", background: "var(--accent-softer)" }}
        >
          <div>
            <p className="text-sm font-medium" style={{ color: "var(--fg)", fontFamily: "system-ui, sans-serif" }}>
              Logout from all devices
            </p>
            <p className="text-xs mt-0.5" style={{ color: "var(--muted)", fontFamily: "system-ui, sans-serif" }}>
              Recommended for account safety.
            </p>
          </div>
          <Switch
            checked={logoutFromAllDevices ?? true}
            onCheckedChange={(val) => form.setValue("logoutFromAllDevices", val)}
          />
        </div>

        {isValid && requirementsMet && <Captcha {...captcha} />}

        {serverError && (
          <p className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>{serverError}</p>
        )}

        <button
          type="submit"
          disabled={isPending || !captchaCode || !isValid || !requirementsMet}
          className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
        >
          {isPending ? (
            <><Loader size={16} style={{ animation: "oidc-spin 1s linear infinite" }} /><span>Saving...</span></>
          ) : (
            <><span>Set Password</span><ArrowRight size={16} /></>
          )}
        </button>
      </form>

      <Link to="/login" className="oidc-sci-fi-link text-sm text-center">
        Back to sign in
      </Link>
    </div>
  );
};
