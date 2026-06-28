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
import { Button } from "@/components/ui-kits/button/button";
import { PasswordInput } from "@/components/password-input";
import { z } from "zod";
import { Input } from "@/components/ui-kits/input/input";

import { useNavigate } from "react-router-dom";
import { showErrorToast } from "@/hooks/use-toast";
import { useAccountActivation } from "@blocks-idp/iam/hooks/use-account";
import { useEffect, useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { Captcha } from "@/components/captcha";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { zodResolver } from "@hookform/resolvers/zod";

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

  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
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
        firstname: values.firstname,
        lastname: values.lastname,
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
        className="flex flex-col gap-4"
      >
        <FormField
          control={form.control}
          name="firstname"
          render={({ field }) => (
            <FormItem>
              <FormLabel>First Name</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <FormField
          control={form.control}
          name="lastname"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Last Name</FormLabel>
              <FormControl>
                <Input {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

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

        {requirementsMet && isValid && <Captcha {...captcha} />}
        <Button
          type="submit"
          className="w-full"
          disabled={isPending || !captchaCode || !requirementsMet || !isValid}
        >
          Activate
        </Button>
      </form>
    </Form>
  );
};
