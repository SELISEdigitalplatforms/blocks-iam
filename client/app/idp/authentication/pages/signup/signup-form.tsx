import { Captcha } from "@/components/captcha";
import { Button } from "@/components/ui-kits/button/button";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { Input } from "@/components/ui-kits/input/input";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useSignupByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { LoginOption } from "@blocks-idp/authentication/models/auth-configuration.model";
import { useCaptcha } from "@blocks-idp/captcha/hooks/use-captcha";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link } from "react-router-dom";
import { useNavigate } from "react-router-dom";
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
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
  const navigate = useNavigate();
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

  const onSubmitHandler = async (values: z.infer<typeof signupFormSchema>) => {
    try {
      const res = await mutateAsync({
        ...values,
        captchaCode,
      });
      if (!res.isSuccess) {
        resetCaptcha();
        return showErrorToast({ errors: res.errors });
      }
      navigate(`/signup-email-sent?email=${values.email}`);
    } catch (error) {
      resetCaptcha();
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };

  useEffect(() => {
    if (!isValid && captchaCode) resetCaptcha();
  }, [captchaCode, isValid, resetCaptcha]);

  return (
    <div className="flex flex-col gap-4">
      <Form {...form}>
        <form onSubmit={form.handleSubmit(onSubmitHandler)}>
          {emailSignUpEnabled && (
            <div className="grid gap-4">
              <FormField
                control={form.control}
                name="email"
                render={({ field }) => (
                  <FormItem>
                    <FormLabel>Email</FormLabel>
                    <FormControl>
                      <Input
                        type="email"
                        placeholder="Enter your email"
                        {...field}
                      />
                    </FormControl>
                    <FormMessage />
                  </FormItem>
                )}
              />
              {isValid && <Captcha {...captcha} />}

              <div className="mt-2 flex justify-start gap-2 text-sm text-foreground">
                <Checkbox
                  id="terms"
                  checked={isChecked}
                  onCheckedChange={(checked) => setIsChecked(!!checked)}
                  className="mt-1 shrink-0"
                />
                <label
                  htmlFor="terms"
                  className="cursor-pointer text-sm font-medium peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
                >
                  I agree to the{" "}
                  <Link
                    to="https://selisegroup.com/software-development-terms/"
                    className="text-primary underline"
                    target="_blank"
                  >
                    Terms of Services{" "}
                  </Link>
                  and acknowledge that I have read the{" "}
                  <Link
                    to="https://selisegroup.com/privacy-policy/"
                    className="text-primary underline"
                    target="_blank"
                  >
                    Privacy policy.
                  </Link>
                </label>
              </div>
              <Button
                type="submit"
                variant="primary"
                className="w-full rounded"
                disabled={isPending || !isValid || !captchaCode || !isChecked}
              >
                Continue
              </Button>
            </div>
          )}
        </form>
      </Form>

      {ssoSignUpEnabled && emailSignUpEnabled && (
        <div className="my-2 flex items-center">
          <hr className="flex-grow border" />
          <span className="mx-2 text-xs text-low-emphasis">OR</span>
          <hr className="flex-grow border" />
        </div>
      )}

      {ssoSignUpEnabled &&
        loginOption?.allowedGrantTypes.includes(GRANT_TYPES.social) && (
          <SsoSignin loginOption={loginOption} />
        )}

      <div className="mt-3 flex items-center justify-center">
        <div className="flex items-center text-medium-emphasis">
          <p>Already a member?</p>
          <Link to="/login" className="ml-2 inline-block text-sm text-primary">
            Log in
          </Link>
        </div>
      </div>
    </div>
  );
};
