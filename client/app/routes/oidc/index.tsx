import { useEffect, useRef, useState } from "react";
import { useSearchParams, useNavigate } from "react-router-dom";
import { OIDCPermissionWrapper } from "@blocks-idp/authentication/pages/oidc/permission-wrapper";
import { OIDCSignin } from "@blocks-idp/authentication/pages/oidc/oidc-signin";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { useAuthStore } from "@/store/useAuthStore";
import { Loader } from "lucide-react";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { getRuntimeEnv } from "@/lib/runtime-env";

export default function OidcIndexPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const { setAuthenticated } = useAuthStore();
  const [isExchanging, setIsExchanging] = useState(false);
  const hasStartedExchange = useRef(false);
  const { clientId, redirectUri, tenantId } = useOIDCContext();

  const code = searchParams.get("code");
  const userName = searchParams.get("userName");

  // Derive fallback values so the exchange works even if localStorage is stale/missing
  const effectiveClientId = clientId || getRuntimeEnv("BLOCKS_OIDC_CLIENT_ID") || undefined;
  const effectiveRedirectUri = redirectUri || `${window.location.origin}/oidc`;
  const effectiveTenantId = tenantId || searchParams.get("tenant_id") || undefined;

  useEffect(() => {
    if (!code || !effectiveClientId) return;
    if (hasStartedExchange.current) return;

    const codeVerifier = sessionStorage.getItem("oidc-code-verifier");
    if (!codeVerifier) {
      navigate("/oidc/error");
      return;
    }

    hasStartedExchange.current = true;
    setIsExchanging(true);
    authService.verifyOidc({
      code,
      clientId: effectiveClientId,
      redirectUri: effectiveRedirectUri,
      codeVerifier,
      tenantId: effectiveTenantId,
    })
      .then(() => {
        sessionStorage.removeItem("oidc-code-verifier");
        setAuthenticated();
        window.location.replace(`${window.location.origin}/`);
      })
      .catch(() => {
        hasStartedExchange.current = false;
        navigate("/oidc/error");
      })
      .finally(() => setIsExchanging(false));
  }, [effectiveClientId, code, navigate, effectiveRedirectUri, setAuthenticated]);

  if (code) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Loader className="h-12 w-12 animate-spin text-gray-500" />
      </div>
    );
  }

  if (userName && userName.trim() !== "") {
    return <OIDCPermissionWrapper />;
  }

  return <OIDCSignin />;
}
