import { useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import { useAuthStore } from "@/store/useAuthStore";
import { getRuntimeEnv } from "@/lib/runtime-env";

export default function SSOCallbackPage() {
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

    const callbackUrl = new URL("/api/oidc/oidc/callback");
    if (code) callbackUrl.searchParams.set("code", code);
    if (state) callbackUrl.searchParams.set("state", state);

    callbackUrl.searchParams.set(
      "tenant_id",
      getRuntimeEnv("BLOCKS_X_BLOCKS_KEY"),
    );
    window.location.href = callbackUrl.toString();
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
