import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { Link, useNavigate } from "react-router-dom";
import { z } from "zod";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { AUTH_ENDPOINTS } from "@blocks-idp/authentication/constants/endpoint.constant";
import React, { useRef, useState } from "react";
import { Captcha } from "@/components/captcha";
import { useTheme } from "@/hooks/use-theme";
import { isErrorWithErrors } from "@/lib/error";
import {
  getCurrentOIDCParams,
  buildOIDCNavigationUrl,
} from "@blocks-idp/authentication/utils/oidc-utils";
import { useGetLoginOptions } from "@blocks-idp/authentication/hooks/use-auth";
import { SsoSignin } from "@blocks-idp/authentication/pages/login/sso-signin";
import { useAuthStore } from "@/store/useAuthStore";
import { sha256 } from "js-sha256";
import { OidcAccountInfo, OidcAccountSelector } from "./oidc-account-selector";
import { useOidcAuthAnimation } from "./oidc-auth-shell";
import { ArrowRight, Eye, EyeOff, Loader } from "lucide-react";

const base64UrlEncode = (bytes: Uint8Array) => {
  const binary = String.fromCharCode(...bytes);
  return btoa(binary)
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
};

const generatePkcePair = async () => {
  const verifierBytes = crypto.getRandomValues(new Uint8Array(32));
  const verifier = base64UrlEncode(verifierBytes);
  const digestHex = sha256(verifier);
  const digestBytes = new Uint8Array(
    digestHex.match(/.{1,2}/g)!.map((byte: string) => parseInt(byte, 16)),
  );
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
  const animCtx = useOidcAuthAnimation();
  const { data: loginOption } = useGetLoginOptions(tenantId, true);
  const [token, setToken] = useState("");
  const [accounts, setAccounts] = useState<OidcAccountInfo[]>([]);
  const [isSelectingAccount, setIsSelectingAccount] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  const [lastAttemptedEmail, setLastAttemptedEmail] = useState("");
  const [showActivationError, setShowActivationError] = useState(false);
  const [activeCodeChallenge, setActiveCodeChallenge] = useState(codeChallenge);
  const [activeCodeChallengeMethod, setActiveCodeChallengeMethod] = useState(
    codeChallengeMethod || "S256",
  );
  const formRef = useRef<HTMLFormElement>(null);

  const isAuthenticating =
    isLoading ||
    animCtx?.phase === "submitting" ||
    animCtx?.phase === "succeeded";

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("oidc-animate-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("oidc-animate-shake");
  }

  const form = useForm<OidcLoginFormValues>({
    defaultValues: {
      username: "",
      password: "",
    },
    resolver: zodResolver(oidcLoginFormSchema),
  });

  const {
    formState: { submitCount },
  } = form;
  const googleSiteKey = getRuntimeEnv("BLOCKS_GOOGLE_SITE_KEY") || "";
  const isTokenNeed = submitCount >= 3;

  const activationUrl = buildOIDCNavigationUrl("/oidc/activation");

  const ensurePkceState = async () => {
    // External relying party (e.g. Dependency-Track) already supplied its own
    // code_challenge in the authorize/login URL. Its matching code_verifier lives
    // on the RP's own origin and is exchanged there, so we MUST forward the
    // challenge verbatim and must NOT generate our own or touch sessionStorage —
    // doing so would bind the auth code to a challenge whose verifier the RP
    // never has, causing "PKCE code_verifier is invalid" at token exchange.
    if (codeChallenge) {
      return {
        codeChallenge,
        codeChallengeMethod: codeChallengeMethod || "S256",
      };
    }

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
    setServerError(null);
    animCtx?.startAnimation();

    try {
      // Always provide a PKCE pair and keep the verifier in session storage for the callback exchange.
      const {
        codeChallenge: effectiveCodeChallenge,
        codeChallengeMethod: effectiveCodeChallengeMethod,
      } = await ensurePkceState();

      // Use provided tenantId; if absent, fallback to configured tenant key when available.
      const finalTenantId =
        tenantId || getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || undefined;
      const codeVerifier = sessionStorage.getItem("oidc-code-verifier");

      const payload = {
        username: values.username,
        password: values.password,
        client_id: clientId,
        redirect_uri: redirectUri,
        scope: scope || "",
        state: state || "",
        nonce: nonce || "",
        code_challenge: effectiveCodeChallenge,
        code_challenge_method: effectiveCodeChallengeMethod,
        tenant_id: finalTenantId || "",
      };

      // Use fetch with redirect: 'manual' to handle redirects explicitly
      // The backend will redirect to the redirect_uri with the authorization code
      const response = await fetch(AUTH_ENDPOINTS.OIDC_LOGIN, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          // Include tenant_id as X-Blocks-Key header for tenant validation middleware
          "X-Blocks-Key": finalTenantId || "",
        },
        body: JSON.stringify(payload),
      });

      if (response.ok) {
        const data = await response.json();
        await animCtx?.succeedAnimation();
        window.location.href = data.redirect_uri;
      }

      // Handle 3xx redirects (302, 303, etc.)
      // if (response.status >= 300 && response.status < 400) {
      //   const location = response.headers.get('Location');
      //   if (location) {
      //     // Use window.location to navigate - this bypasses CORS and lets the browser handle the redirect
      //     window.location.href = location;
      //     return;
      //   }
      // }

      // Parse response as JSON
      let data;
      try {
        const contentType = response.headers.get("content-type");
        if (contentType && contentType.includes("application/json")) {
          data = await response.json();
        } else if (!response.ok) {
          const errMsg = `Server error (HTTP ${response.status})`;
          shake();
          setServerError(errMsg);
          await animCtx?.failAnimation(errMsg);
          setIsLoading(false);
          return;
        } else {
          // Success but no JSON content - code is in URL
          await animCtx?.succeedAnimation();
          return;
        }
      } catch (parseError) {
        console.error("Error parsing response:", parseError);
        animCtx?.resetAnimation();
        setIsLoading(false);
        return;
      }

      // If response is OK, check for account selection required
      if (response.ok || response.status === 200) {
        // Check if backend requires account selection
        if (
          data?.status === "account_selection_required" &&
          data?.accounts?.length > 0
        ) {
          animCtx?.resetAnimation();
          setAccounts(data.accounts);
          setIsSelectingAccount(true);
          setIsLoading(false);
          return;
        }

        // Otherwise code is already in URL, let the SPA handle it
        await animCtx?.succeedAnimation();
        return;
      }

      // Handle error responses
      const errorMsg =
        data?.error_description || data?.message || "Login failed";
      const errorCode = data?.error;

      shake();
      if (errorCode === "account_locked") {
        const msg = "Your account is locked. Please contact support or reset your password.";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      } else if (errorCode === "account_not_verified") {
        animCtx?.resetAnimation();
        setLastAttemptedEmail(values.username);
        setShowActivationError(true);
      } else if (errorCode === "invalid_credentials") {
        const msg = "Invalid email or password. Please try again.";
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      } else {
        setServerError(errorMsg);
        await animCtx?.failAnimation(errorMsg);
      }
    } catch (error) {
      console.error("Login error:", error);
      const msg = "An unexpected error occurred during login. Please try again.";
      shake();
      setServerError(msg);
      await animCtx?.failAnimation(msg);
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
      const effectiveTenantId =
        account.tenant_id ||
        tenantId ||
        getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") ||
        "";

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
        setServerError("Failed to select account");
      }
    } catch (error: unknown) {
      if (isErrorWithErrors(error)) {
        const errorCode = error.errors?.error;
        const errorDesc = error.errors?.error_description;
        const message = Array.isArray(errorDesc)
          ? errorDesc[0]
          : (errorDesc || errorCode || "Failed to select account") as string;
        setServerError(message);
      } else {
        setServerError("Failed to select account");
      }
    }
  };

  if (isSelectingAccount && accounts.length > 0) {
    return (
      <OidcAccountSelector
        accounts={accounts}
        onAccountSelect={handleAccountSelect}
        isLoading={isLoading}
      />
    );
  }

  const showPasswordLogin = true;
  // Show social login by default - if loginOption is available, check the config; otherwise default to true
  const showSocialLogin = !!loginOption?.ssoInfo?.length;

  if (showActivationError) {
    return (
      <div className="flex flex-col gap-4">
        <div
          className="rounded-lg p-4"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
            Account Not Verified
          </p>
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
            Your account needs to be activated. Check your email for the activation link.
          </p>
        </div>
        <div className="flex flex-col gap-2">
          <button
            type="button"
            onClick={() => (window.location.href = activationUrl)}
            className="oidc-sci-fi-btn-outline w-full"
          >
            Activate Account
          </button>
          <button
            type="button"
            onClick={() => { setShowActivationError(false); form.reset(); }}
            className="oidc-sci-fi-btn-outline w-full"
            style={{ opacity: 0.7 }}
          >
            Back to Login
          </button>
        </div>
      </div>
    );
  }

  return (
    <>
      {showPasswordLogin && (
        <Form {...form}>
          <form
            ref={formRef}
            onSubmit={form.handleSubmit(onSubmitHandler, shake)}
            className="flex flex-col gap-5 w-full"
            noValidate
          >
            {/* Email */}
            <FormField
              control={form.control}
              name="username"
              render={({ field }) => (
                <FormItem>
                  <div className="flex flex-col gap-2">
                    <label htmlFor="oidc-email" className="oidc-sci-fi-label">
                      Work Email
                    </label>
                    <FormControl>
                      <input
                        id="oidc-email"
                        type="email"
                        autoComplete="email"
                        placeholder="name@company.com"
                        className="oidc-sci-fi-input"
                        aria-invalid={!!form.formState.errors.username}
                        disabled={isAuthenticating}
                        {...field}
                      />
                    </FormControl>
                    <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                  </div>
                </FormItem>
              )}
            />

            {/* Password */}
            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <div className="flex flex-col gap-2">
                    <div className="flex justify-between items-center">
                      <label htmlFor="oidc-password" className="oidc-sci-fi-label">
                        Password
                      </label>
                      <Link
                        to="/forgot-password"
                        className="oidc-sci-fi-link"
                        style={{ fontSize: "0.75rem" }}
                      >
                        Forgot?
                      </Link>
                    </div>
                    <FormControl>
                      <div className="relative">
                        <input
                          id="oidc-password"
                          type={showPassword ? "text" : "password"}
                          autoComplete="current-password"
                          placeholder="••••••••"
                          className="oidc-sci-fi-input"
                          style={{ paddingRight: "2.75rem" }}
                          aria-invalid={!!form.formState.errors.password}
                          disabled={isAuthenticating}
                          {...field}
                        />
                        <button
                          type="button"
                          tabIndex={-1}
                          onClick={() => setShowPassword(v => !v)}
                          className="absolute right-3 top-1/2 -translate-y-1/2"
                          style={{ color: "var(--muted)", background: "none", border: "none", cursor: "pointer", padding: 0 }}
                          aria-label={showPassword ? "Hide password" : "Show password"}
                        >
                          {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                        </button>
                      </div>
                    </FormControl>
                    <FormMessage className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }} />
                  </div>
                </FormItem>
              )}
            />

            {/* CAPTCHA (shown after 3 failed attempts) */}
            {isTokenNeed &&
              React.createElement(Captcha, {
                type: "reCaptcha-v2-checkbox",
                siteKey: googleSiteKey,
                theme: theme === "dark" ? "dark" : "light",
                onVerify: (t: string) => setToken(t),
                onExpired: () => setToken(""),
                onError: () => setToken(""),
              } as any)}

            {/* Inline server error */}
            {serverError && (
              <p className="text-sm" style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}>
                {serverError}
              </p>
            )}

            {/* Submit */}
            <button
              type="submit"
              disabled={isAuthenticating || (isTokenNeed && !token)}
              className="oidc-sci-fi-btn mt-3 w-full flex items-center justify-center gap-2"
            >
              {isAuthenticating ? (
                <>
                  <Loader size={16} className="oidc-spin-slow" />
                  <span>Authenticating...</span>
                </>
              ) : (
                <>
                  <span>Login</span>
                  <ArrowRight size={16} />
                </>
              )}
            </button>
          </form>
        </Form>
      )}

      {showPasswordLogin && showSocialLogin && (
        <div
          className="my-2 mt-4 flex items-center gap-3"
        >
          <div className="flex-1 border-t" style={{ borderColor: "var(--border)" }} />
          <span className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>or</span>
          <div className="flex-1 border-t" style={{ borderColor: "var(--border)" }} />
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

      <div className="mt-4">
        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          Not a member?{" "}
          <Link to="/signup" className="oidc-sci-fi-link" style={{ fontSize: "0.75rem" }}>
            Create an account
          </Link>
        </p>
      </div>
    </>
  );
};
