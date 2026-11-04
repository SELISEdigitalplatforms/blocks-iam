
import { Button } from "@/components/ui-kits/button/button";
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormMessage,
} from "@/components/ui-kits/form/form";
import { InputOTP, InputOTPGroup, InputOTPSlot } from "@/components/ui-kits/input-otp/input-otp";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";
import { AUTH_ENDPOINTS } from "@blocks-idp/authentication/constants/endpoint.constant";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  extractOIDCParams,
  resolveLoginReturnTarget,
} from "@blocks-idp/authentication/utils/oidc-utils";
import { zodResolver } from "@hookform/resolvers/zod";
import { useLocation, useNavigate } from "react-router";
import { parseAsInteger, parseAsString, useQueryStates } from "nuqs";
import { ArrowRight, Clock, Loader, RotateCcw } from "lucide-react";
import { useState } from "react";
import { useT } from "@seliseblocks/genesis-os/i18n";

import { useForm } from "react-hook-form";
import { z } from "zod";
import { isAppRedirectUrl, navigateToSameOrigin, navigateToUrl } from "@/lib/utils";
import { OidcAppHandoff } from "@blocks-idp/authentication/pages/oidc/oidc-app-handoff";

const CustomInputOTPSlot = ({ index }: { index: number }) => {
  return (
    <InputOTPSlot
      index={index}
      className="oidc-sci-fi-otp-slot h-12 w-[46px] rounded-sm first:rounded-l-sm last:rounded-r-sm"
    />
  );
};
const formSchema = z.object({
  code: z.string().min(6),
});

// The second leg's answer when the sign-in behind this page is gone: the server keeps it only a
// few minutes (IdpConstants.OidcStateCacheTtlSeconds), so no code can finish it any more.
const MFA_SESSION_EXPIRED = "invalid_mfa_session";

// The tenant of the login being completed, NOT the tenant of the IdP serving this page.
// `BLOCKS_X_BLOCKS_KEY` is this bundle's own build-time project key -- always the root IdP --
// so using it alone sent every construct's second leg to the root tenant. The server resolves
// the challenge's user against the tenant named by x-blocks-key, so the construct's user was
// looked up in the root database: verify failed with `invalid_credentials`, resend with
// `user_not_found`. /oidc/login's first leg already prefers the URL's tenant (see
// oidc-login-form); verify and resend keep the second leg on the same tenant.
const loginTenantId = (): string | undefined =>
  extractOIDCParams().tenantId || getRuntimeEnv("BLOCKS_X_BLOCKS_KEY") || undefined;

type MfaCheckFromProps = {
  /** Lets the page swap its heading and instructions once the sign-in has expired. */
  onSessionExpired?: () => void;
};

export const MfaCheckFrom = ({ onSessionExpired }: MfaCheckFromProps = {}) => {
  const { t } = useT("iam.mfa-check");
  const navigate = useNavigate();
  const location = useLocation();
  const animCtx = useOidcAuthAnimation();
  const { data: oidcUiConfig } = useOidcUiConfig();
  const [{ mfa_id: mfaId, mfa_type: mfaType, returnUrl }] = useQueryStates({
    mfa_id: parseAsString.withDefault(""),
    mfa_type: parseAsInteger.withDefault(0),
    // Carried through from a device-flow (RFC 8628) login that required MFA — the
    // backend's post-MFA response has no redirect_uri for a device-flow client, so
    // this is where the browser goes back to instead.
    returnUrl: parseAsString.withDefault(""),
  });
  const [isLoading, setIsLoading] = useState(false);
  const [serverError, setServerError] = useState<string | null>(null);
  // Set when the client is a native app; OidcAppHandoff then replaces the form and opens the app.
  const [appHandoffUrl, setAppHandoffUrl] = useState<string | null>(null);
  const [sessionExpired, setSessionExpired] = useState(false);
  const { setAuthenticated } = useAuthStore();

  const expireSession = () => {
    setServerError(null);
    setSessionExpired(true);
    onSessionExpired?.();
  };
  // An email challenge expires with the sign-in, and a resend cannot extend it.
  const { remainingTime, resend } = useResendOtp({
    mfaId,
    tenantId: loginTenantId(),
    onExpired: expireSession,
  });

  // Both ways out lead to the sign-in of this same flow: the OS console's or the construct
  // app's client, tenant and PKCE challenge. A page without them falls back to the application.
  const backToSignInLabel = resolveLoginReturnTarget(location.pathname.startsWith("/oidc"), {
    preferOidcLogin: true,
  }).external
    ? t("MFA_CHECK_FORM.BACK_TO_APPLICATION")
    : t("MFA_CHECK_FORM.BACK_TO_SIGN_IN");

  const form = useForm<{ code: string }>({
    resolver: zodResolver(formSchema),
    defaultValues: {
      code: "",
    },
  });

  if (appHandoffUrl) return <OidcAppHandoff url={appHandoffUrl} />;

  if (!oidcUiConfig?.template) return null;
  const mfaCopy = oidcUiConfig.template.pages.mfa;

  // Expired, or a link that never carried a challenge: a code cannot help, only a new sign-in.
  if (sessionExpired || !mfaId) {
    return (
      <div role="alert" className="flex flex-col items-center gap-5 text-center">
        <div className="flex h-14 w-14 items-center justify-center rounded-full border bg-(--warn-soft) border-(--warn-border)">
          <Clock className="h-7 w-7 text-(--warn)" aria-hidden />
        </div>
        <p className="max-w-sm text-sm leading-relaxed text-(--fg)">{t("MFA_CHECK_FORM.SESSION_EXPIRED_MESSAGE")}</p>
        <LoginReturnLink
          preferOidcLogin
          className="oidc-sci-fi-btn flex w-full items-center justify-center gap-2 no-underline"
        >
          <span>{backToSignInLabel}</span>
          <ArrowRight size={16} />
        </LoginReturnLink>
      </div>
    );
  }

  const isAuthenticating =
    isLoading ||
    animCtx?.phase === "submitting" ||
    animCtx?.phase === "succeeded";

  /**
   * Hands a login the server returned a redirect target for back to the client: a native app
   * through the hand-off block (no success animation, which would unmount it), anything else
   * through a full-page navigation once the success animation has played.
   */
  const followRedirectUrl = async (redirectUrl: string) => {
    if (isAppRedirectUrl(redirectUrl)) {
      animCtx?.resetAnimation();
      setAppHandoffUrl(redirectUrl);
      return;
    }
    await animCtx?.succeedAnimation();
    if (!navigateToUrl(redirectUrl)) {
      const msg = t("MFA_CHECK_FORM.NO_REDIRECT_TARGET");
      setServerError(msg);
      await animCtx?.failAnimation(msg);
    }
  };

  /** Sends a verified login on: to its redirect target, else the device-flow returnUrl, else the profile. */
  const completeVerifiedLogin = async (redirectUrl: string | undefined) => {
    if (redirectUrl) {
      await followRedirectUrl(redirectUrl);
      return;
    }
    if (returnUrl) {
      // returnUrl comes from the query string, so it may only point back at this origin.
      // A refused device-flow return must not drop the user on the console instead.
      if (navigateToSameOrigin(returnUrl)) {
        await animCtx?.succeedAnimation();
        return;
      }
      const msg = t("MFA_CHECK_FORM.INVALID_RETURN_ADDRESS");
      setServerError(msg);
      await animCtx?.failAnimation(msg);
      return;
    }
    await animCtx?.succeedAnimation();
    navigate("/app/profile");
  };

  const submitHandler = async ({ code }: { code: string }) => {
    setIsLoading(true);
    setServerError(null);
    animCtx?.startAnimation();

    try {
      const finalTenantId = loginTenantId();

      const payload = {
        mfa_id: mfaId,
        mfa_code: code,
        tenant_id: finalTenantId || "",
      };

      const response = await fetch(AUTH_ENDPOINTS.OIDC_LOGIN, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Blocks-Key": finalTenantId || "",
        },
        body: JSON.stringify(payload),
      });

      const contentType = response.headers.get("content-type");
      const data = contentType?.includes("application/json")
        ? await response.json()
        : null;

      if (response.ok) {
        setAuthenticated();
        const redirectUrl =
          data?.redirect_uri || data?.redirect_url || data?.redirectUrl;
        await completeVerifiedLogin(redirectUrl);
        return;
      }

      const errorCode = data?.error;
      const errorMsg =
        data?.error_description || data?.message || t("MFA_CHECK_FORM.VERIFICATION_FAILED");

      if (errorCode === "account_locked") {
        const msg = t("MFA_CHECK_FORM.ACCOUNT_LOCKED");
        setServerError(msg);
        await animCtx?.failAnimation(msg);
      } else if (errorCode === "invalid_mfa_code") {
        const msg = t("MFA_CHECK_FORM.INVALID_CODE");
        setServerError(msg);
        await animCtx?.failAnimation(msg);
        form.reset();
      } else if (errorCode === MFA_SESSION_EXPIRED) {
        expireSession();
        await animCtx?.failAnimation(t("MFA_CHECK_FORM.SESSION_EXPIRED_MESSAGE"));
      } else {
        setServerError(errorMsg);
        await animCtx?.failAnimation(errorMsg);
      }
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({
          errors: error.errors.error_description || t("MFA_CHECK_FORM.SOMETHING_WENT_WRONG"),
        });
      } else {
        showErrorToast({ errors: t("MFA_CHECK_FORM.SOMETHING_WENT_WRONG") });
      }
    } finally {
      setIsLoading(false);
    }
  };

  const { isValid } = form.formState;

  return (
    <Form {...form}>
      <form onSubmit={form.handleSubmit(submitHandler)} className="flex flex-col gap-5">
        <FormField
          control={form.control}
          name="code"
          render={({ field }) => (
            <FormItem>
              <FormControl>
                <InputOTP maxLength={6} {...field}>
                  <InputOTPGroup className="w-full justify-between gap-6">
                    <CustomInputOTPSlot index={0} />
                    <CustomInputOTPSlot index={1} />
                    <CustomInputOTPSlot index={2} />
                    <CustomInputOTPSlot index={3} />
                    <CustomInputOTPSlot index={4} />
                    <CustomInputOTPSlot index={5} />
                  </InputOTPGroup>
                </InputOTP>
              </FormControl>
              <FormMessage className="text-xs text-(--danger)" />
            </FormItem>
          )}
        />

        {mfaType === 2 && mfaCopy.resendButton && (
          <div className="flex items-center justify-end text-sm">
            <Button
              type="button"
              variant="link"
              className="oidc-sci-fi-link flex items-center gap-1.5 p-0 text-sm font-medium no-underline!"
              onClick={resend}
              disabled={!!remainingTime}
            >
              <RotateCcw size={14} />
              {mfaCopy.resendButton}
              {remainingTime > 0 &&
                ` (${Math.floor(remainingTime / 60)}:${String(remainingTime % 60).padStart(2, "0")})`}
            </Button>
          </div>
        )}

        {serverError && (
          <p
            className="text-sm"
            style={{
              color: "var(--danger)",
              fontFamily: "system-ui, sans-serif",
            }}
          >
            {serverError}
          </p>
        )}

        <button
          type="submit"
          className="oidc-sci-fi-btn w-full flex items-center justify-center gap-2"
          disabled={!isValid || isAuthenticating}
        >
          {isAuthenticating ? (
            <>
              <Loader size={16} className="oidc-spin-slow" />
              <span>{mfaCopy.submitButton === "Verify" ? t("MFA_CHECK_FORM.VERIFYING") : `${mfaCopy.submitButton}...`}</span>
            </>
          ) : (
            <>
              <span>{mfaCopy.submitButton}</span>
              <ArrowRight size={16} />
            </>
          )}
        </button>

        {/* Always there: a lost phone, a mail that never comes or a locked account must not strand the user. */}
        <div className="flex justify-center text-sm">
          <LoginReturnLink preferOidcLogin className="oidc-sci-fi-link text-sm font-medium no-underline!">
            {backToSignInLabel}
          </LoginReturnLink>
        </div>
      </form>
    </Form>
  );
};
