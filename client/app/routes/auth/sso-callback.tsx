import { useEffect, useMemo, useRef } from "react";
import { useParams, useSearchParams } from "react-router";
import { useAuthStore } from "@seliseblocks/genesis-os";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { getCurrentOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";

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

    const callbackUrl = new URL(
      "/api/oidc/callback",
      getRuntimeEnv("BLOCKS_IAM_BASE_URL"),
    );
    if (code) callbackUrl.searchParams.set("code", code);
    if (state) callbackUrl.searchParams.set("state", state);
    if (tenantId) callbackUrl.searchParams.set("tenant_id", tenantId);
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
