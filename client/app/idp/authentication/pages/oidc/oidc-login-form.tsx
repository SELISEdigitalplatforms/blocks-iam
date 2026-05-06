import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/ui-kits/input/input";
import { PasswordInput } from "@/components/password-input";
import { Button } from "@/components/ui-kits/button/button";
import { Link } from "react-router-dom";
import { z } from "zod";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { showErrorToast } from "@/hooks/use-toast";
import { useState } from "react";
import { Captcha } from "@/components/captcha";
import { useTheme } from "@/hooks/use-theme";
import { isErrorWithErrors } from "@/lib/error";
import { OidcAccountSelector, OidcAccountInfo } from "./oidc-account-selector";
import { getCurrentOIDCParams, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { OidcSocialSignin } from "./oidc-social-signin";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";

const oidcLoginFormSchema = z.object({
  username: z.string().email("Invalid email address"),
  password: z.string().min(1, "Password is required"),
});

type OidcLoginFormValues = z.infer<typeof oidcLoginFormSchema>;

interface OidcLoginFormProps {
  clientId: string;
  redirectUri: string;
  scope?: string;
  state?: string;
  nonce?: string;
  codeChallenge?: string;
  codeChallengeMethod?: string;
}

export const OidcLoginForm = ({
  clientId,
  redirectUri,
  scope,
  state,
  nonce,
  codeChallenge,
  codeChallengeMethod,
}: OidcLoginFormProps) => {
  const { theme } = useTheme();
  const { data: loginOption, isLoading: isLoginOptionLoading } = useGetLoginOptions();
  const [token, setToken] = useState("");
  const [accounts, setAccounts] = useState<OidcAccountInfo[]>([]);
  const [isSelectingAccount, setIsSelectingAccount] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [lastAttemptedEmail, setLastAttemptedEmail] = useState("");
  const [showActivationError, setShowActivationError] = useState(false);

  const form = useForm<OidcLoginFormValues>({
    defaultValues: {
      username: "",
      password: "",
    },
    resolver: zodResolver(oidcLoginFormSchema),
  });

  const { formState: { submitCount } } = form;
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const isTokenNeed = submitCount >= 3;

  const forgotPasswordUrl = buildOIDCNavigationUrl("/forgot-password");
  const signUpUrl = buildOIDCNavigationUrl("/oidc/signup");
  const activationUrl = buildOIDCNavigationUrl(`/activation`);

  const onSubmitHandler = async (values: OidcLoginFormValues) => {
    setIsLoading(true);
    setShowActivationError(false);

    try {
      const response = await authService.signinByOidcEmail({
        username: values.username,
        password: values.password,
        clientId,
        redirectUri,
        scope,
        state,
        nonce,
        code_challenge: codeChallenge,
        code_challenge_method: codeChallengeMethod,
      });

      // Check for MFA requirement
      if (response.enable_mfa) {
        const mfaPath = `/mfa-check?mfa_id=${response.mfaId}&mfa_type=${response.mfaType}`;
        window.location.href = buildOIDCNavigationUrl(mfaPath);
        return;
      }

      // Check if account selection is required
      if (response.status === "account_selection_required" && response.accounts?.length > 1) {
        setAccounts(response.accounts);
        setIsSelectingAccount(true);
      } else if (response.redirect_url) {
        // Direct redirect (single account case)
        window.location.href = response.redirect_url;
      } else if (response.code) {
        // Handle code response if needed
        window.location.href = response.redirect_url || redirectUri;
      } else {
        showErrorToast({ errors: "Unexpected response from server" });
      }
    } catch (error: unknown) {
      if (isErrorWithErrors(error)) {
        const errorMsg = error.errors.error_description || "Something went wrong";
        const errorCode = error.errors.error;

        // Handle account locked
        if (errorCode === "account_locked") {
          showErrorToast({ errors: "Your account is locked. Please contact support or reset your password." });
        }
        // Handle account not verified
        else if (errorCode === "account_not_verified") {
          setLastAttemptedEmail(values.username);
          setShowActivationError(true);
        }
        // Handle invalid credentials
        else if (errorCode === "invalid_credentials") {
          showErrorToast({ errors: "Invalid email or password. Please try again." });
        } else {
          showErrorToast({ errors: errorMsg });
        }
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleAccountSelect = async (account: OidcAccountInfo) => {
    try {
      const response = await authService.selectOidcAccount({
        userId: account.user_id,
        tenantId: account.tenant_id,
        clientId,
        redirectUri,
        scope,
        state,
        nonce,
        code_challenge: codeChallenge,
        code_challenge_method: codeChallengeMethod,
      });

      // Redirect to the authorize endpoint with the code
      if (response.redirect_url) {
        window.location.href = response.redirect_url;
      } else {
        showErrorToast({ errors: "Failed to select account" });
      }
    } catch (error: unknown) {
      if (isErrorWithErrors(error)) {
        const message = Array.isArray(error.errors.error_description)
          ? error.errors.error_description[0]
          : error.errors.error_description || "Failed to select account";
        throw new Error(message);
      } else {
        throw new Error("Failed to select account");
      }
    }
  };

  if (isLoginOptionLoading) {
    return (
      <div className="flex flex-col gap-4">
        {/* Email label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-10 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Password label + input */}
        <div className="flex flex-col gap-2">
          <Skeleton className="h-4 w-16 rounded" />
          <Skeleton className="h-10 w-full rounded-md" />
        </div>
        {/* Forgot password */}
        <Skeleton className="ml-auto h-4 w-28 rounded" />
        {/* Login button */}
        <Skeleton className="h-10 w-full rounded-md" />
      </div>
    );
  }

  if (isSelectingAccount && accounts.length > 0) {
    return <OidcAccountSelector accounts={accounts} onAccountSelect={handleAccountSelect} isLoading={isLoading} />;
  }

  const showPasswordLogin = loginOption?.allowedGrantTypes.includes(GRANT_TYPES.password);
  const showSocialLogin = loginOption?.allowedGrantTypes.includes(GRANT_TYPES.social);

  if (showActivationError) {
    return (
      <div className="flex flex-col gap-4 text-center">
        <div className="rounded-lg bg-destructive/10 p-4">
          <p className="mb-2 text-sm font-medium text-destructive">Account Not Verified</p>
          <p className="text-sm text-destructive/80">
            Your account needs to be activated. Check your email for the activation link.
          </p>
        </div>
        <div className="flex flex-col gap-2">
          <Button 
            onClick={() => window.location.href = activationUrl}
            className="w-full rounded"
            variant="outline"
          >
            Activate Account
          </Button>
          <Button 
            onClick={() => {
              setShowActivationError(false);
              form.reset();
            }}
            variant="ghost"
            className="w-full rounded"
          >
            Back to Login
          </Button>
        </div>
      </div>
    );
  }

  return (
    <>
      {showPasswordLogin && (
        <Form {...form}>
          <form onSubmit={form.handleSubmit(onSubmitHandler)} className="flex flex-col gap-4">
            <FormField
              control={form.control}
              name="username"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Email</FormLabel>
                  <FormControl>
                    <Input placeholder="Enter your email" {...field} disabled={isLoading} />
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
                    <PasswordInput placeholder="Enter your password" {...field} disabled={isLoading} />
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

            <Button type="submit" className="w-full rounded" disabled={isLoading || (isTokenNeed && !token)}>
              {isLoading ? "Signing in..." : "Log in"}
            </Button>
          </form>
        </Form>
      )}

      {showPasswordLogin && showSocialLogin && (
        <div className="my-2 mt-4 flex items-center">
          <hr className="flex-grow border" />
          <span className="mx-2 text-xs text-low-emphasis">OR</span>
          <hr className="flex-grow border" />
        </div>
      )}

      {showSocialLogin && <OidcSocialSignin clientId={clientId} loginOption={loginOption} />}

      <div className="mt-3 flex items-center justify-center">
        <div className="flex items-center text-medium-emphasis">
          <p>Not a member?</p>
          <Link to={signUpUrl} className="ml-2 inline-block text-sm text-primary">
            Sign up
          </Link>
        </div>
      </div>
    </>
  );
};
