import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { isAppRedirectUrl } from "@/lib/utils";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { sanitizeProviderUrl } from "@blocks-idp/authentication/utils/sanitize-provider-url.util";
import { getSsoStartErrorMessage } from "@blocks-idp/authentication/utils/sso-start-errors";
import {
  OIDC_APP_HANDOFF_STORAGE_KEY,
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY,
  OIDC_SSO_RETURN_URI_STORAGE_KEY,
} from "@blocks-idp/authentication/utils/oidc-utils";
import { tStatic } from "@seliseblocks/genesis-os/i18n";

const IAM_SHARED_MODULE = "iam.shared";

/** The OIDC request a social sign-in belongs to; the provider's callback resolves it on return. */
export type OidcSocialSignInContext = {
  clientId?: string;
  redirectUri?: string;
  scope?: string;
  state?: string;
  nonce?: string;
  code_challenge?: string;
  code_challenge_method?: string;
  tenantId?: string;
  // Device-flow (RFC 8628) logins have no redirect_uri — this is the device
  // verification page to send the browser back to once the provider returns.
  returnUrl?: string;
};

/**
 * A native-app login cannot be finished by navigating the callback page to the backend
 * (the backend's 302 to a custom scheme leaves a blank page), so mark it for the
 * callback page to fetch the result as JSON and show the hand-off instead. Any other
 * login clears the marker, so a stale one never changes it.
 */
const updateAppHandoffMarker = (redirectUri: string | undefined) => {
  if (isAppRedirectUrl(redirectUri ?? "")) {
    sessionStorage.setItem(OIDC_APP_HANDOFF_STORAGE_KEY, "1");
  } else {
    sessionStorage.removeItem(OIDC_APP_HANDOFF_STORAGE_KEY);
  }
};

/** Kept for the error page, in case the server loses the sign-in while the user is at the provider. */
const rememberSsoReturnUri = (redirectUri: string | undefined) => {
  try {
    if (redirectUri) sessionStorage.setItem(OIDC_SSO_RETURN_URI_STORAGE_KEY, redirectUri);
    else sessionStorage.removeItem(OIDC_SSO_RETURN_URI_STORAGE_KEY);
  } catch {
    // Storage blocked: the error page falls back to IAM's own login.
  }
};

/**
 * Starts signing in with a social provider inside an OIDC request: asks IAM for the provider's
 * authorization URL, keeps what the return trip needs, and leaves for the provider. A failure
 * is shown as a toast. Used by the provider buttons and by the sign-in page's automatic start.
 *
 * `replace` leaves without keeping this page in the browser history, so Back from the
 * provider returns to the application instead of to a page that would start it again.
 *
 * Resolves true once the browser is on its way to the provider, false when it stays.
 */
export async function startOidcSocialSignIn({
  provider,
  providerClientId,
  oidcContext,
  replace = false,
}: {
  provider: string;
  providerClientId: string;
  oidcContext: OidcSocialSignInContext;
  replace?: boolean;
}): Promise<boolean> {
  try {
    const iamBaseUrl = getRuntimeEnv("BLOCKS_IAM_BASE_URL");
    const res = await authService.signinByOidcEmail({
      provider,
      clientId: oidcContext.clientId || "",
      redirectUri: oidcContext.redirectUri || "",
      scope: oidcContext.scope,
      state: oidcContext.state,
      nonce: oidcContext.nonce,
      code_challenge: oidcContext.code_challenge,
      code_challenge_method: oidcContext.code_challenge_method,
      tenantId: oidcContext.tenantId,
      provider_client_id: providerClientId,
      provider_redirect_uri: `${iamBaseUrl}/oidc/${provider}/callback/${oidcContext.tenantId}`,
    });

    if (res.error) {
      showErrorToast({ errors: getSsoStartErrorMessage(res) });
      return false;
    }
    if (!res.authorizationUrl) {
      showErrorToast({ errors: tStatic(IAM_SHARED_MODULE, "SSO_SIGNIN_CARD.NO_AUTHORIZATION_URL") });
      return false;
    }

    if (oidcContext.returnUrl) {
      sessionStorage.setItem(OIDC_DEVICE_RETURN_URL_STORAGE_KEY, oidcContext.returnUrl);
    }
    rememberSsoReturnUri(oidcContext.redirectUri);
    updateAppHandoffMarker(oidcContext.redirectUri);

    const providerUrl = sanitizeProviderUrl(res.authorizationUrl);
    if (replace) window.location.replace(providerUrl);
    else window.location.href = providerUrl;
    return true;
  } catch (error) {
    console.error("SSO Sign-in error:", error);
    showErrorToast({
      errors: isErrorWithErrors(error)
        ? getSsoStartErrorMessage(error)
        : tStatic(IAM_SHARED_MODULE, "SSO_SIGNIN_CARD.SOMETHING_WENT_WRONG"),
    });
    return false;
  }
}
