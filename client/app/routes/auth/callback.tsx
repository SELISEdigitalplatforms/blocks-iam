import { useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import { useAuthStore } from "@seliseblocks/blocks-kit";

export default function LoginCallbackPage() {
  const [searchParams] = useSearchParams();
  const hasProcessed = useRef(false);
  const { setAuthenticated } = useAuthStore();

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const error = searchParams.get("error");
  const tenantId = searchParams.get("tenant_id");

  useEffect(() => {
    if (hasProcessed.current) return;
    hasProcessed.current = true;

     const API_BASES = { IDP : "/api"};
        // const apiBaseUrl = API_BASES.IDP;

    const apiBaseUrl = API_BASES.IDP.startsWith("http") ? API_BASES.IDP : window.location.origin;
    const callbackUrl = new URL("/api/idp/callback", apiBaseUrl);

    // const apiBaseUrl = import.meta.env.BLOCKS_IAM_BASE_URL?.startsWith("http")
    //   ? import.meta.env.BLOCKS_IAM_BASE_URL
    //   : window.location.origin;
    // const callbackUrl = new URL("/api/idp/callback", apiBaseUrl);

    // Forward the callback parameters to backend
    if (code) callbackUrl.searchParams.set("code", code);
    if (state) callbackUrl.searchParams.set("state", state);
    if (error) callbackUrl.searchParams.set("error", error);
    if (tenantId) callbackUrl.searchParams.set("tenant_id", tenantId);

    // Make fetch request with tenant_id in both query param and header
    const headers: Record<string, string> = {
      "Content-Type": "application/json",
    };
    if (tenantId) {
      headers["X-Blocks-Key"] = tenantId;
    }

    fetch(callbackUrl.toString(), { headers, credentials: "include" })
      .then((res) => {
        if (res.ok) {
          setAuthenticated();
          window.location.href = "/console";
        } else {
          window.location.href = "/login?error=callback_failed";
        }
      })
      .catch(() => {
        window.location.href = "/login?error=callback_error";
      });
  }, [code, state, error, tenantId, setAuthenticated]);

  // return (
  //   <div className="flex min-h-screen items-center justify-center">
  //     <Loader className="h-12 w-12 animate-spin text-gray-500" />
  //   </div>
  // );

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