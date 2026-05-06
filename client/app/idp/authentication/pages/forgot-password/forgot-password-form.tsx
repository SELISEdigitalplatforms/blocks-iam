import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { Input } from "@/components/ui-kits/input/input";
import { Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { forgotPasswordFormSchema, forgotPasswordFormDefaultValue } from "./utils";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { z } from "zod";
import { showErrorToast } from "@/hooks/use-toast";
import { useNavigate } from "react-router-dom";
// import { Captcha } from "@/components/captcha"; // REMOVED: Not available
import { useEffect } from "react";
import { useAccountRecover } from "@blocks-idp/iam/hooks/use-account";
import { isErrorWithErrors } from "@/lib/error";
// import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha"; // REMOVED: Hook not found
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

interface ForgotPasswordFormProps {
  mode?: "default" | "oidc";
}

export const ForgotPasswordForm = ({ mode = "default" }: ForgotPasswordFormProps) => {
  const navigate = useNavigate();
  const form = useForm({
    defaultValues: forgotPasswordFormDefaultValue,
    resolver: zodResolver(forgotPasswordFormSchema),
  });
  const { isPending, mutateAsync } = useAccountRecover();
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type: "reCaptcha-v2-checkbox",
  });
  const { isValid } = form.formState;

  const onSubmitHandler = async (values: z.infer<typeof forgotPasswordFormSchema>) => {
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      const redirectUrl = mode === "oidc" 
        ? buildOIDCNavigationUrl(`/email-sent-confirmation?email=${values.email}`)
        : `/oidc/email-sent-confirmation?email=${values.email}`;
      navigate(redirectUrl);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  const backToLoginUrl = mode === "oidc" ? buildOIDCNavigationUrl("/") : "/login";

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)}>
        <div className="grid grid-cols-1 gap-4">
          <FormField
            control={form.control}
            name="email"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Email</FormLabel>
                <FormControl>
                  <Input type="email" placeholder="Enter your email" {...field} />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          {isValid && <Captcha {...captcha} />}

          <div className="my-2 flex items-center">
            <hr className="flex-grow border-gray-300" />
            <span className="mx-2 text-xs text-gray-500">OR</span>
            <hr className="flex-grow border-gray-300" />
          </div>

          <Button
            type="submit"
            className="w-full rounded"
            disabled={isPending || !isValid || !captchaCode}
          >
            Continue
          </Button>
        </div>
        <div className="mt-4 text-center text-base text-foreground">
          Already a member?{" "}
          <Link to={backToLoginUrl} className="text-primary hover:underline">
            Log in
          </Link>
        </div>
      </form>
    </Form>
  );
};
