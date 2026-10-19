import { useEffect, useRef, useState } from "react";
import { useParams, useSearchParams } from "react-router";
import { Loader } from "lucide-react";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { getSelfBaseUrl } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import { isAppRedirectUrl, navigateToSameOrigin, navigateToUrl } from "@/lib/utils";
import {
  OIDC_APP_HANDOFF_STORAGE_KEY,
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY,
} from "@blocks-idp/authentication/utils/oidc-utils";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  OidcAuthShell,
  OidcFooter,
} from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { OidcAppHandoff } from "@blocks-idp/authentication/pages/oidc/oidc-app-handoff";
import { OIDC_LOGIN_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";
import "@blocks-idp/authentication/pages/oidc/sci-fi-oidc.css";

const DEFAULT_CALLBACK_ERROR = "server_error";
const DEFAULT_CALLBACK_ERROR_DESCRIPTION = "Sign-in could not be completed.";

type CallbackErrorBody = { error?: unknown; error_description?: unknown } | null;

/**
 * Sends a failed JSON-mode callback to the standalone error page. Only the error strings are
 * taken from the response; the destination is always this origin's `/oidc/error`.
 */
const navigateToCallbackError = (body: CallbackErrorBody, tenantId: string | undefined) => {
  const error =
    typeof body?.error === "string" && body.error ? body.error : DEFAULT_CALLBACK_ERROR;
  const description =
    typeof body?.error_description === "string" && body.error_description
      ? body.error_description
      : DEFAULT_CALLBACK_ERROR_DESCRIPTION;
  navigateToSameOrigin(
    `/oidc/error?error=${encodeURIComponent(error)}` +
      `&error_description=${encodeURIComponent(description)}` +
      `&tenant_id=${encodeURIComponent(tenantId ?? "")}`,
  );
};

/** The native-app hand-off, inside the same themed shell as the login page it started from. */
function SsoCallbackAppHandoff({ url, tenantId }: { url: string; tenantId?: string }) {
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={OIDC_LOGIN_PANEL}
      theme={template.theme}
      logoUrlLight={template.branding.logoUrlLight}
      logoUrlDark={template.branding.logoUrlDark}
      brandName={template.branding.brandName}
      heading={template.pages.login.heading}
      headingDimFirst={3}
      successTitle="Access Granted"
      successSubtitle="Redirecting to your application…"
      showCorners={false}
      footerNote={<OidcFooter footerText={template.pages.shared.footerText} />}
    >
      <OidcAppHandoff url={url} />
    </OidcAuthShell>
  );
}

export default function SSOCallbackPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const [searchParams] = useSearchParams();
  const hasProcessed = useRef(false);
  const [appHandoffUrl, setAppHandoffUrl] = useState<string | null>(null);
  const { setAuthenticated } = useAuthStore();

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const error = searchParams.get("error");
  const errorDescription = searchParams.get("error_description");


  useEffect(() => {
    if (hasProcessed.current) return;

    hasProcessed.current = true;

    const callbackUrl = new URL("/api/oidc/callback", getSelfBaseUrl());
    if (code) callbackUrl.searchParams.set("code", code);
    if (state) callbackUrl.searchParams.set("state", state);
    if (tenantId) callbackUrl.searchParams.set("tenant_id", tenantId);
    // A provider that refuses sends `error` and no `code` -- most often the user pressing Cancel
    // on its consent screen. Forwarding it lets the backend resolve the state back into the
    // original OIDC request and return the user to the login page with something to read.
    // Dropping it here instead would leave the backend answering a codeless callback.
    if (error) callbackUrl.searchParams.set("error", error);
    if (errorDescription) callbackUrl.searchParams.set("error_description", errorDescription);

    const deviceReturnUrl = sessionStorage.getItem(
      OIDC_DEVICE_RETURN_URL_STORAGE_KEY,
    );
    // Read once and always cleared, so a stale marker never changes a later sign-in.
    const isAppHandoff = sessionStorage.getItem(OIDC_APP_HANDOFF_STORAGE_KEY) === "1";
    sessionStorage.removeItem(OIDC_APP_HANDOFF_STORAGE_KEY);

    if (!deviceReturnUrl && isAppHandoff && code && state) {
      // Native-app login: a navigation would end on the backend's 302 to a custom scheme, which
      // leaves a blank page when the app does not open. Fetch the redirect as JSON instead and
      // show the hand-off, which keeps a "Return to the app" link on screen.
      fetch(callbackUrl.toString(), { headers: { Accept: "application/json" } })
        .then(async (response) => {
          const data = await response.json().catch(() => null);
          const redirectUri =
            typeof data?.redirect_uri === "string" ? data.redirect_uri : "";
          if (!response.ok || !redirectUri) {
            navigateToCallbackError(data, tenantId);
            return;
          }
          if (isAppRedirectUrl(redirectUri)) {
            setAppHandoffUrl(redirectUri);
            return;
          }
          if (!navigateToUrl(redirectUri)) navigateToCallbackError(null, tenantId);
        })
        .catch(() => navigateToCallbackError(null, tenantId));
      return;
    }

    if (!deviceReturnUrl) {
      // Normal (non device-flow) social login: the backend answers this call with
      // an HTTP redirect straight to the client's redirect_uri, which only works
      // as a real page navigation.
      window.location.href = callbackUrl.toString();
      return;
    }

    // Device flow (RFC 8628): this call's only job is to authenticate the user —
    // the backend always answers with `{ success: true }` JSON here (no
    // redirect_uri to follow), same contract as password login, so fetch it and
    // finish the trip back to the device verification page ourselves.
    sessionStorage.removeItem(OIDC_DEVICE_RETURN_URL_STORAGE_KEY);
    // Asking for JSON outright is what separates this leg from a browser navigation. The backend
    // answers navigations with a redirect to a page carrying the error; fetch would follow that
    // silently to an HTML 200, leaving `response.ok` true and the failure unreported.
    fetch(callbackUrl.toString(), { headers: { Accept: "application/json" } })
      .then(async (response) => {
        const data = await response.json().catch(() => null);
        if (!response.ok) {
          showErrorToast({
            errors: data?.error_description || "Social sign in failed. Please try again.",
          });
        }
        window.location.href = data?.redirect_uri || deviceReturnUrl;
      })
      .catch(() => {
        showErrorToast({ errors: "Social sign in failed. Please try again." });
        window.location.href = deviceReturnUrl;
      });
  }, [code, state, error, errorDescription, tenantId, setAuthenticated]);

  if (appHandoffUrl) {
    return <SsoCallbackAppHandoff url={appHandoffUrl} tenantId={tenantId} />;
  }

  if ((code && state) || error) {
    return (
      <>
        <style>{`
          @keyframes breathe {
            0%, 100% {
              transform: scaleY(1);
            }
            50% {
              transform: scaleY(0.85);
            }
          }
          .animate-breathe {
            animation: breathe 2s ease-in-out infinite;
            transform-origin: center;
          }
        `}</style>
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-background">
          <img
            src="/Icon.svg"
            alt="Loading"
            className="h-16 w-16 animate-breathe"
          />
        </div>
      </>
    );
  }
}
