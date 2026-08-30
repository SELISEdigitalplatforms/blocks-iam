import { useEffect, useMemo, useRef } from "react";
import { useParams, useSearchParams } from "react-router";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { getSelfBaseUrl } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";
import {
  getCurrentOIDCParams,
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY,
} from "@blocks-idp/authentication/utils/oidc-utils";

export default function SSOCallbackPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const [searchParams] = useSearchParams();
  const hasProcessed = useRef(false);
  const { setAuthenticated } = useAuthStore();

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const error = searchParams.get("error");


  useEffect(() => {
    if (hasProcessed.current) return;

    hasProcessed.current = true;

    const callbackUrl = new URL("/api/oidc/callback", getSelfBaseUrl());
    if (code) callbackUrl.searchParams.set("code", code);
    if (state) callbackUrl.searchParams.set("state", state);
    if (tenantId) callbackUrl.searchParams.set("tenant_id", tenantId);

    const deviceReturnUrl = sessionStorage.getItem(
      OIDC_DEVICE_RETURN_URL_STORAGE_KEY,
    );

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
    fetch(callbackUrl.toString())
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
  }, [code, state, error, tenantId, setAuthenticated]);

  if (code && state) {
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
