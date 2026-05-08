import { Form, FormControl, FormField, FormItem, FormLabel, FormMessage } from "@/components/ui-kits/form/form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Input } from "@/components/ui-kits/input/input";
import { PasswordInput } from "@/components/password-input";
import { Button } from "@/components/ui-kits/button/button";
import { Link, useNavigate } from "react-router-dom";
import { z } from "zod";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { AUTH_ENDPOINTS } from "@blocks-idp/authentication/constants/endpoint.constant";
import { showErrorToast } from "@/hooks/use-toast";
import { useState } from "react";
import { Captcha } from "@/components/captcha";
import { useTheme } from "@/hooks/use-theme";
import { isErrorWithErrors } from "@/lib/error";
import { OidcAccountSelector, OidcAccountInfo } from "./oidc-account-selector";
import { getCurrentOIDCParams, buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { SsoSignin } from "@blocks-idp/authentication/pages/login/sso-signin";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { GRANT_TYPES } from "@blocks-idp/authentication/constants/authentication.constant";
import { useAuthStore } from "@/store/useAuthStore";
import sha256 from 'js-sha256';

const base64UrlEncode = (bytes: Uint8Array) => {
  const binary = String.fromCharCode(...bytes);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
};

const generatePkcePair = async () => {
  const verifierBytes = crypto.getRandomValues(new Uint8Array(32));
  const verifier = base64UrlEncode(verifierBytes);
  const digestHex = sha256(verifier);
  const digestBytes = new Uint8Array(digestHex.match(/.{1,2}/g)!.map(byte => parseInt(byte, 16)));
  const challenge = base64UrlEncode(digestBytes);

  return { verifier, challenge };
};

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
  tenantId?: string;
}

export const OidcLoginForm = ({
  clientId,
  redirectUri,
  scope,
  state,
  nonce,
  codeChallenge,
  codeChallengeMethod,
  tenantId,
}: OidcLoginFormProps) => {
  const navigate = useNavigate();
  const { theme } = useTheme();
  const { data: loginOption } = useGetLoginOptions(tenantId, true);
  const [token, setToken] = useState("");
  const [accounts, setAccounts] = useState<OidcAccountInfo[]>([]);
  const [isSelectingAccount, setIsSelectingAccount] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [lastAttemptedEmail, setLastAttemptedEmail] = useState("");
  const [showActivationError, setShowActivationError] = useState(false);
  const [activeCodeChallenge, setActiveCodeChallenge] = useState(codeChallenge);
  const [activeCodeChallengeMethod, setActiveCodeChallengeMethod] = useState(codeChallengeMethod || "S256");

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

  const forgotPasswordUrl = buildOIDCNavigationUrl("/oidc/forgot-password");
  const signUpUrl = buildOIDCNavigationUrl("/oidc/signup");
  const activationUrl = buildOIDCNavigationUrl("/oidc/activation");

  const ensurePkceState = async () => {
    const existingVerifier = sessionStorage.getItem("oidc-code-verifier");

    if (existingVerifier && activeCodeChallenge) {
      return {
        codeChallenge: activeCodeChallenge,
        codeChallengeMethod: activeCodeChallengeMethod || "S256",
      };
    }

    const { verifier, challenge } = await generatePkcePair();
    sessionStorage.setItem("oidc-code-verifier", verifier);
    setActiveCodeChallenge(challenge);
    setActiveCodeChallengeMethod("S256");

    return {
      codeChallenge: challenge,
      codeChallengeMethod: "S256",
    };
  };

  const onSubmitHandler = async (values: OidcLoginFormValues) => {
    setIsLoading(true);
    setShowActivationError(false);

    try {
      // Always provide a PKCE pair and keep the verifier in session storage for the callback exchange.
      const {
        codeChallenge: effectiveCodeChallenge,
        codeChallengeMethod: effectiveCodeChallengeMethod,
      } = await ensurePkceState();

      // Use provided tenantId; if absent, fallback to configured tenant key when available.
      const finalTenantId = tenantId || getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || undefined;
      const codeVerifier = sessionStorage.getItem("oidc-code-verifier");
      
      const payload = {
        username: values.username,
        password: values.password,
        client_id: clientId,
        redirect_uri: redirectUri,
        scope: scope || '',
        state: state || '',
        nonce: nonce || '',
        code_challenge: effectiveCodeChallenge,
        code_challenge_method: effectiveCodeChallengeMethod,
        tenant_id: finalTenantId || '',
      };

      // Use fetch with redirect: 'manual' to handle redirects explicitly
      // The backend will redirect to the redirect_uri with the authorization code
      const response = await fetch(AUTH_ENDPOINTS.OIDC_LOGIN, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          // Include tenant_id as X-Blocks-Key header for tenant validation middleware
          'X-Blocks-Key': finalTenantId || '',
        },
        body: JSON.stringify(payload),
        redirect: 'manual', // Handle redirects manually to deal with cross-origin redirects
      });

      // Handle 3xx redirects (302, 303, etc.)
      if (response.status >= 300 && response.status < 400) {
        const location = response.headers.get('Location');
        if (location) {
          // Use window.location to navigate - this bypasses CORS and lets the browser handle the redirect
          window.location.href = location;
          return;
        }
      }

      // Parse response as JSON
      let data;
      try {
        const contentType = response.headers.get('content-type');
        if (contentType && contentType.includes('application/json')) {
          data = await response.json();
        } else if (!response.ok) {
          showErrorToast({ errors: `Server error (HTTP ${response.status})` });
          setIsLoading(false);
          return;
        } else {
          // Success but no JSON content - code is in URL
          return;
        }
      } catch (parseError) {
        console.error('Error parsing response:', parseError);
        showErrorToast({ errors: `Server error: Unable to process response` });
        setIsLoading(false);
        return;
      }

      // If response is OK, check for account selection required
      if (response.ok || response.status === 200) {
        // Check if backend requires account selection
        if (data?.status === "account_selection_required" && data?.accounts?.length > 0) {
          setAccounts(data.accounts);
          setIsSelectingAccount(true);
          setIsLoading(false);
          return;
        }
        
        // Otherwise code is already in URL, let the SPA handle it
        return;
      }

      // Handle error responses
      const errorMsg = data?.error_description || data?.message || "Login failed";
      const errorCode = data?.error;

      if (errorCode === "account_locked") {
        showErrorToast({ errors: "Your account is locked. Please contact support or reset your password." });
      } else if (errorCode === "account_not_verified") {
        setLastAttemptedEmail(values.username);
        setShowActivationError(true);
      } else if (errorCode === "invalid_credentials") {
        showErrorToast({ errors: "Invalid email or password. Please try again." });
      } else {
        showErrorToast({ errors: errorMsg });
      }
    } catch (error) {
      console.error('Login error:', error);
      showErrorToast({ errors: "An unexpected error occurred during login. Please try again." });
    } finally {
      setIsLoading(false);
    }
  };

  const handleAccountSelect = async (account: OidcAccountInfo) => {
    try {
      const {
        codeChallenge: effectiveCodeChallenge,
        codeChallengeMethod: effectiveCodeChallengeMethod,
      } = await ensurePkceState();
      const effectiveTenantId = account.tenant_id || tenantId || getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || "";

      // Continue account selection with the same PKCE challenge to keep flow consistent.
      const response = await authService.selectOidcAccount({
        userId: account.user_id,
        tenantId: effectiveTenantId,
        clientId,
        redirectUri,
        scope,
        state,
        nonce,
        code_challenge: effectiveCodeChallenge,
        code_challenge_method: effectiveCodeChallengeMethod,
      });

      // Redirect to permission screen or authorization endpoint
      if (response.redirect_url) {
        window.location.href = response.redirect_url;
      } else {
        showErrorToast({ errors: "Failed to select account" });
      }
    } catch (error: unknown) {
      if (isErrorWithErrors(error)) {
        const errorCode = error.errors?.error;
        const errorDesc = error.errors?.error_description;
        const message = Array.isArray(errorDesc)
          ? errorDesc[0]
          : errorDesc || errorCode || "Failed to select account";
        showErrorToast({ errors: message });
      } else {
        showErrorToast({ errors: "Failed to select account" });
      }
    }
  };

  if (isSelectingAccount && accounts.length > 0) {
    return <OidcAccountSelector accounts={accounts} onAccountSelect={handleAccountSelect} isLoading={isLoading} />;
  }

  // Default to showing password login if loginOption is not available or fails to load
  // Don't wait for login options - show form immediately with sensible defaults
  const showPasswordLogin = loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.password) ?? true;
  // Show social login by default - if loginOption is available, check the config; otherwise default to true
  const showSocialLogin = loginOption?.allowedGrantTypes?.includes(GRANT_TYPES.social) ?? true;

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

      {showSocialLogin && (
        <SsoSignin
          loginOption={loginOption}
          mode="oidc"
          oidcContext={{
            clientId,
            redirectUri,
            scope,
            state,
            nonce,
            code_challenge: activeCodeChallenge,
            code_challenge_method: activeCodeChallengeMethod,
            tenantId,
          }}
        />
      )}

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
