import { getRuntimeEnv } from "@/lib/runtime-env";
import { Link, useSearchParams } from "react-router";
import { useForm } from "react-hook-form";
import { forgotPasswordFormSchema, forgotPasswordFormDefaultValue } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate } from "react-router";
import { Captcha } from "@/components/captcha";
import { useEffect, useState } from "react";
import { useAccountRecover } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { ArrowRight } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";

export const OIDCForgotPasswordForm = () => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const tenantId =
    searchParams.get("tenant_id") ||
    searchParams.get("tenantId") ||
    undefined;
  const [serverError, setServerError] = useState<string | null>(null);

  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(forgotPasswordFormSchema),
  });
  const { isPending, mutateAsync } = useAccountRecover();

  const { data: oidcUiConfig, captchaEnabled } = useOidcUiConfig();
  const googleSiteKey =
    oidcUiConfig?.captcha?.key || getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";

  const { captcha, code: captchaCode, reset: resetCaptcha } = useCaptcha({
    siteKey: googleSiteKey,
    type: oidcUiConfig?.captcha?.provider === "hcaptcha" ? "hCaptcha" : "reCaptcha-v2-checkbox",
    generator: oidcUiConfig?.captcha?.generator,
  });
  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<typeof forgotPasswordFormSchema>) => {
    setServerError(null);
    try {
      const res = await mutateAsync({ ...values, captchaCode, tenantId });
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
      if (res?.isSuccess) {
        navigate(`/oidc/forgot-email-sent?email=${values.email}`);
        return;
      }
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
        <h2 className="text-xl font-semibold mb-2 font-sans text-[var(--fg)]">
          Reset Password
        </h2>
        <p className="text-sm font-sans text-[var(--muted)]">
          Enter your email and we&apos;ll dispatch a recovery link.
        </p>
      </div>

      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-5">
          <FormField
            control={form.control}
            name="email"
            render={({ field }) => (
              <FormItem>
                <FormLabel className="oidc-sci-fi-label">Email</FormLabel>
                <FormControl>
                  <input
                    type="email"
                    placeholder="name@company.com"
                    autoComplete="email"
                    className="oidc-sci-fi-input"
                    {...field}
                  />
                </FormControl>
                <FormMessage className="text-xs text-[var(--danger)]" />
              </FormItem>
            )}
          />

          {captchaEnabled && isValid && <Captcha {...captcha} />}

          {serverError && (
            <p className="text-sm font-sans text-[var(--danger)]">{serverError}</p>
          )}

          {isPending ? (
            <Skeleton className="h-10 w-full rounded-md" />
          ) : (
            <button
              type="submit"
              disabled={!isValid || (captchaEnabled && !captchaCode)}
              className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
            >
              <span>Send Recovery Link</span><ArrowRight size={16} />
            </button>
          )}
        </form>
      </Form>

      <Link to="/login" className="oidc-sci-fi-link text-sm text-center">
        Back to login
      </Link>
    </div>
  );
};
