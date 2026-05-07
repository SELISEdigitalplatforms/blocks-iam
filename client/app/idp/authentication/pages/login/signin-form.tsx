import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { signinFormDefaultValue, signinFormSchema } from "./schema";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/ui-kits/input/input";
import { PasswordInput } from "@/components/password-input";
import { Link } from "react-router-dom";
import { Button } from "@/components/ui-kits/button/button";
import { z } from "zod";
import { useSigninByEmail } from "@blocks-idp/authentication/hooks/use-auth";
import { useAuthStore } from "@/store/useAuthStore";
import { showErrorToast } from "@/hooks/use-toast";
import { useNavigate } from "react-router-dom";
import { useState } from "react";
import { Captcha } from "@/components/captcha";
import { useTheme } from "@/hooks/use-theme";
import { isErrorWithErrors } from "@/lib/error";
import { buildOIDCNavigationUrl, getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

type SigninFormProps = {
  mode?: "default" | "oidc";
  oidcContext?: {
    clientId?: string;
    scope?: string;
    state?: string;
    nonce?: string;
    redirectUri?: string;
  };
};

export const SigninForm = ({ mode = "default", oidcContext }: SigninFormProps) => {
  const { theme } = useTheme();
  const navigate = useNavigate();
  const { setAuthenticated, setTokens } = useAuthStore();
  const [token, setToken] = useState("");
  const form = useForm({
    defaultValues: signinFormDefaultValue,
    resolver: zodResolver(signinFormSchema),
  });
  const { isPending, mutateAsync } = useSigninByEmail();
  const onSubmitHandler = async (values: z.infer<typeof signinFormSchema>) => {
    try {
      const requestPayload = mode === "oidc"
        ? {
            ...values,
            clientId: oidcContext?.clientId,
            scope: oidcContext?.scope,
            state: oidcContext?.state,
            redirectUri: oidcContext?.redirectUri,
            nonce: oidcContext?.nonce,
          }
        : values;

      const res = await mutateAsync(requestPayload);
      if (res.enable_mfa) {
        const mfaPath = `/mfa-check?mfa_id=${res.mfaId}&mfa_type=${res.mfaType}`;
        return navigate(mode === "oidc" ? buildOIDCNavigationUrl(mfaPath) : mfaPath);
      }

      // Root tenant FE uses only HttpOnly cookies; no token handling from response needed

      setAuthenticated();
      if (mode === "oidc") {
        const params = getCurrentOIDCParams();
        params.set("userName", values.username);
        navigate(`/oidc/permission?${params.toString()}`);
      } else {
        navigate("/console");
      }
    } catch (error: unknown) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors.error_description || `Something went wrong` });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    }
  };
  const {
    formState: { submitCount },
  } = form;
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const isTokenNeed = submitCount >= 3;
  const forgotPasswordUrl = mode === "oidc"
    ? (() => {
        const target = buildOIDCNavigationUrl("/forgot-password");
        return `${target}${target.includes("?") ? "&" : "?"}mode=oidc`;
      })()
    : "/forgot-password";

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
        <FormField
          control={form.control}
          name="username"
          render={({ field }) => (
            <FormItem>
              <FormLabel>Email</FormLabel>
              <FormControl>
                <Input placeholder="Enter your email" {...field} />
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
                <PasswordInput placeholder="Enter your password" {...field} />
              </FormControl>
              <FormMessage />
            </FormItem>
          )}
        />

        <Link to={forgotPasswordUrl} className="ml-auto inline-block text-sm text-primary">
          Forgot password?
        </Link>
        {isTokenNeed && (
          <Captcha
            type="reCaptcha-v2-checkbox"
            siteKey={googleSiteKey}
            theme={theme === "dark" ? "dark" : "light"}
            onVerify={(token) => setToken(token)}
            onExpired={() => setToken("")}
            onError={() => setToken("")}
          />
        )}

        <Button type="submit" className="w-full rounded" disabled={isPending || (isTokenNeed && !token)}>
          Log in
        </Button>
      </form>
    </Form>
  );
};
