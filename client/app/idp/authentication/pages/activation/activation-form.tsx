import { useForm } from "react-hook-form";
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

import { useNavigate, useSearchParams } from "react-router-dom";
import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useAccountActivation } from "@blocks-idp/authentication/hooks/use-auth";
import { useEffect, useState } from "react";
import { isErrorWithErrors } from "@/lib/error";
import { Captcha } from "@/components/captcha";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { PasswordStrengthChecker } from "../../components/password-strength-checker/password-strength-checker";
import { zodResolver } from "@hookform/resolvers/zod";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

type ActivationFormProps = {
  code?: string;
  mode?: "default" | "oidc";
};

export const ActivationForm = ({ code: propCode, mode = "default" }: ActivationFormProps) => {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const code = propCode || searchParams.get("code") || "";

  const form = useForm({
    defaultValues: activationFormDefaultValue,
    mode: "all",
    reValidateMode: "onChange",
    resolver: zodResolver(activationFormSchema),
  });
  const [requirementsMet, setRequirementsMet] = useState(false);

  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const {
    captcha,
    code: captchaCode,
    reset: resetCaptcha,
  } = useCaptcha({
    siteKey: googleSiteKey,
    type: "reCaptcha-v2-checkbox",
  });
  const { isPending, mutateAsync } = useAccountActivation();

  useEffect(() => {
    if (!requirementsMet && captchaCode) resetCaptcha();
  }, [captchaCode, requirementsMet, resetCaptcha]);

  useEffect(() => {
    if (!code) return navigate(mode === "oidc" ? buildOIDCNavigationUrl("/") : "/login");
  }, [code, navigate, mode]);

  const onSubmitHandler = async (values: z.infer<typeof activationFormSchema>) => {
    try {
      const res = await mutateAsync({
        code: code,
        preventPostEvent: true,
        password: values.password,
        firstname: values.firstname,
        lastname: values.lastname,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      
      if (mode === "oidc") {
        // Return to OIDC login flow
        navigate(buildOIDCNavigationUrl("/"));
      } else {
        return navigate("/activate-success");
      }
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
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
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
          Activate BTN
        </Button>
      </form>
    </Form>
  );
};
