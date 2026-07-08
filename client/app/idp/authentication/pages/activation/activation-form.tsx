import { useForm } from "react-hook-form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { activationFormDefaultValue, activationFormSchema } from "./utils";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { Captcha } from "@/components/captcha";
import { useNavigate } from "react-router-dom";
import { showErrorToast } from "@/hooks/use-toast";
import { useAccountActivation } from "@blocks-idp/iam/hooks/use-account";
import { useEffect, useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowRight, Eye, EyeOff } from "lucide-react";

type ActivationFormProps = {
  code: string;
  tenantId?: string;
};

export const ActivationForm = ({ code, tenantId }: ActivationFormProps) => {
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: activationFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(activationFormSchema),
  });
  const [requirementsMet, setRequirementsMet] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [showConfirmPassword, setShowConfirmPassword] = useState(false);

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

  const onSubmitHandler = async (
    values: z.infer<typeof activationFormSchema>,
  ) => {
    try {
      const res = await mutateAsync({
        code: code,
        preventPostEvent: true,
        password: values.password,
        captchaCode,
        tenantId,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      return navigate("/activate-success");
    } catch (error: unknown) {
      resetCaptcha();
      if (isErrorWithErrors(error)) {
        window.location.reload();
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");
  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form
        onSubmit={form.handleSubmit(onSubmitHandler)}
        className="flex flex-col gap-5"
      >
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel className="oidc-sci-fi-label">Password</FormLabel>
              <FormControl>
                <div className="relative">
                  <input
                    type={showPassword ? "text" : "password"}
                    autoComplete="new-password"
                    placeholder="Enter a strong password"
                    className="oidc-sci-fi-input pr-10"
                    {...field}
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword((s) => !s)}
                    className="absolute inset-y-0 right-2 flex items-center text-[var(--muted)] hover:text-[var(--fg)]"
                    aria-label={showPassword ? "Hide password" : "Show password"}
                  >
                    {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </FormControl>
              <FormMessage className="text-xs text-[var(--danger)]" />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="confirmPassword"
          render={({ field }) => (
            <FormItem>
              <FormLabel className="oidc-sci-fi-label">Confirm Password</FormLabel>
              <FormControl>
                <div className="relative">
                  <input
                    type={showConfirmPassword ? "text" : "password"}
                    autoComplete="new-password"
                    placeholder="Re-enter your password"
                    className="oidc-sci-fi-input pr-10"
                    {...field}
                  />
                  <button
                    type="button"
                    onClick={() => setShowConfirmPassword((s) => !s)}
                    className="absolute inset-y-0 right-2 flex items-center text-[var(--muted)] hover:text-[var(--fg)]"
                    aria-label={showConfirmPassword ? "Hide password" : "Show password"}
                  >
                    {showConfirmPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </FormControl>
              <FormMessage className="text-xs text-[var(--danger)]" />
            </FormItem>
          )}
        />

        <PasswordStrengthChecker
          password={password}
          confirmPassword={confirmPassword}
          onRequirementsMet={setRequirementsMet}
        />

        {captchaEnabled && requirementsMet && isValid && <Captcha {...captcha} />}

        <button
          type="submit"
          disabled={isPending || (captchaEnabled && !captchaCode) || !requirementsMet || !isValid}
          className="oidc-sci-fi-btn mt-2 w-full flex items-center justify-center gap-2"
        >
          <span>Activate</span>
          <ArrowRight size={16} />
        </button>
      </form>
    </Form>
  );
};