import LogoLoadingSpinner from "@/components/loader-spinner/loader-spinner";
import { cn } from "@/lib/utils";
import { LoginOption } from "@blocks-idp/authentication/models/auth.model";
import { SSOSigninCard } from "@blocks-idp/authentication/components/sso-signin-card";
import { useSsoActivation } from "@blocks-idp/authentication/hooks/use-sso-activation";
import { extractOIDCParams } from "@blocks-idp/authentication/utils/oidc-utils";
import { getAvailableSsoProviders } from "@blocks-idp/authentication/utils/sso-providers.util";
import { useMemo } from "react";
import { useParams } from "react-router";

type OidcSsoContext = NonNullable<SsoSigninProps["oidcContext"]>;

/**
 * Social sign-in only works inside an OIDC request (the provider returns to /oidc/.../callback,
 * which resolves the request it belongs to). Pages such as signup carry that request in their URL
 * without passing it down, so read it from there when the caller gave none.
 */
const useOidcSsoContext = (given: SsoSigninProps["oidcContext"]): OidcSsoContext | undefined => {
  const { tenantId: routeTenantId } = useParams<{ tenantId?: string }>();

  return useMemo(() => {
    if (given?.clientId) return given;

    const url = extractOIDCParams();
    if (!url.clientId) return undefined;

    const search = new URLSearchParams(window.location.search);
    return {
      clientId: url.clientId,
      redirectUri: url.redirectUri,
      scope: url.scope,
      state: url.state,
      nonce: url.nonce,
      code_challenge: search.get("code_challenge") || undefined,
      code_challenge_method: search.get("code_challenge_method") || undefined,
      tenantId: given?.tenantId || url.tenantId || routeTenantId,
      returnUrl: url.returnUrl,
    };
  }, [given, routeTenantId]);
};

type SsoSigninProps = {
  loginOption: LoginOption;
  mode?: "default" | "oidc";
  oidcContext?: {
    clientId?: string;
    redirectUri?: string;
    scope?: string;
    state?: string;
    nonce?: string;
    code_challenge?: string;
    code_challenge_method?: string;
    tenantId?: string;
    returnUrl?: string;
  };
  // Every provider button inert, e.g. while the page is already leaving for one of them.
  disabled?: boolean;
};

// Static map so Tailwind's JIT scanner can detect all class names at build
// time. Dynamic template literals like `grid-cols-${n}` are purged in prod.
const GRID_COLS_MAP: Record<number, string> = {
  1: "grid-cols-1",
  2: "grid-cols-2",
  3: "grid-cols-3",
  4: "grid-cols-4",
  5: "grid-cols-5",
  6: "grid-cols-3",
};

export const SsoSignin = ({
  loginOption,
  mode = "default",
  oidcContext,
  disabled = false,
}: SsoSigninProps) => {
  const { isPending } = useSsoActivation();
  const ssoContext = useOidcSsoContext(oidcContext);

  const providers = getAvailableSsoProviders(loginOption);

  const gridColsClass = GRID_COLS_MAP[Math.min(providers.length, 6)];

  return (
    <>
      <div className={cn("grid gap-2", gridColsClass)}>
        {providers.map((item) => (
          <SSOSigninCard
            providerConfig={item}
            key={item.provider}
            labelMode={providers.length === 1 ? "full" : "short"}
            mode={ssoContext ? "oidc" : mode}
            oidcContext={ssoContext}
            disabled={disabled}
          />
        ))}
      </div>

      {isPending && <LogoLoadingSpinner size={48} />}
    </>
  );
};

