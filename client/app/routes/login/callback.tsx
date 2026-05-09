import { useEffect, useRef } from "react";
import { useSearchParams } from "react-router-dom";
import { Loader } from "lucide-react";
import { getRuntimeEnv } from "@/lib/runtime-env";

export default function LoginCallbackPage() {
  const [searchParams] = useSearchParams();
  const hasProcessed = useRef(false);

  const code = searchParams.get("code");
  const state = searchParams.get("state");
  const error = searchParams.get("error");
  const tenantId = searchParams.get("tenant_id");

  useEffect(() => {
    if (hasProcessed.current) return;
    hasProcessed.current = true;

    const apiBaseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL") || "http://localhost:5000";
    const callbackUrl = new URL("/api/idp/callback", apiBaseUrl);

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
          window.location.href = "/";
        } else {
          window.location.href = "/login?error=callback_failed";
        }
      })
      .catch(() => {
        window.location.href = "/login?error=callback_error";
      });
  }, [code, state, error, tenantId]);

  return (
    <div className="flex min-h-screen items-center justify-center">
      <Loader className="h-12 w-12 animate-spin text-gray-500" />
    </div>
  );
}
