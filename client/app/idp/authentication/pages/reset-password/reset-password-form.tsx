import { useForm } from "react-hook-form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Button } from "@/components/ui-kits/button/button";
import { PasswordInput } from "@/components/password-input";
import { Switch } from "@/components/ui-kits/switch/switch";
import { useNavigate } from "react-router-dom";
import { showErrorToast } from "@/hooks/use-toast";
import { useAccountResetPassword } from "@blocks-idp/iam/hooks/use-account";
import { Captcha } from "@/components/captcha";
import { useEffect, useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { PasswordStrengthChecker } from "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  resetPasswordFormSchema,
  ResetPasswordFormValuesType,
  resetPasswordFormDefaultValue,
} from "./utils";

type ResetPasswordFormProps = {
  code: string;
};

export const ResetPasswordForm = ({ code }: ResetPasswordFormProps) => {
  const navigate = useNavigate();
  const form = useForm<ResetPasswordFormValuesType>({
    defaultValues: resetPasswordFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(resetPasswordFormSchema),
  });
  const [requirementsMet, setRequirementsMet] = useState(false);

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({ siteKey: googleSiteKey, type: "reCaptcha-v2-checkbox" });

  const { isPending, mutateAsync } = useAccountResetPassword();

  const { isValid } = form.formState;

  useEffect(() => {
    if (!isValid && !requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, requirementsMet, resetCaptcha]);

  const onSubmitHandler = async (values: ResetPasswordFormValuesType) => {
    try {
      const res = await mutateAsync({
        code: code,
        captchaCode,
        logoutFromAllDevices: values.logoutFromAllDevices,
        password: values.password,
      });

      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      navigate("/reset-password-success");
    } catch (error: unknown) {
      resetCaptcha();
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const password = form.watch("password");
  const confirmPassword = form.watch("confirmPassword");

  return (
    <Form {...form}>
      <form
        onSubmit={form.handleSubmit(onSubmitHandler)}
        className="flex flex-col gap-4"
      >
        <FormField
          control={form.control}
          name="password"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Password</FormLabel>
              <FormControl>
                <PasswordInput {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />
        <FormField
          control={form.control}
          name="confirmPassword"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Confirm Password</FormLabel>
              <FormControl>
                <PasswordInput {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <PasswordStrengthChecker
          password={password}
          confirmPassword={confirmPassword}
          onRequirementsMet={setRequirementsMet}
        />

        <FormField
          control={form.control}
          name="logoutFromAllDevices"
          render={({ field }) => (
            <FormItem>
              <FormControl>
                <div className="border rounded-lg p-4 space-y-2">
                  <div className="flex items-center justify-between">
                    <label
                      htmlFor="logout-all-devices"
                      className="text-sm font-medium text-high-emphasis"
                    >
                      Logout from all devices
                    </label>
                    <Switch
                      checked={field.value}
                      onCheckedChange={field.onChange}
                    />
                  </div>
                  <p className="text-xs text-muted-foreground">
                    Recommended for account safety after resetting your
                    password.
                  </p>
                </div>
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        {isValid && requirementsMet && <Captcha {...captcha} />}

        <Button
          type="submit"
          variant="primary"
          className="w-full"
          disabled={isPending || !captchaCode || !isValid || !requirementsMet}
        >
          Reset Password
        </Button>
      </form>
    </Form>
  );
};
