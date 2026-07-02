import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { useTheme } from "@/hooks/use-theme";
import { useCallback, useMemo } from "react";
import { sanitizeProviderUrl } from "@blocks-idp/authentication/utils/sanitize-provider-url.util";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

type SSOSigninCardProps = {
  providerConfig: ISsoProviderConfigurationWithMeta;
  withLabel?: boolean;
  labelMode?: "full" | "short" | "icon";
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
  };
};

export const SSOSigninCard = ({
  providerConfig,
  withLabel = false,
  labelMode,
  mode = "default",
  oidcContext,
}: SSOSigninCardProps) => {
  const { resolvedTheme } = useTheme();

  const onClickHandler = useCallback(async () => {
    try {
      if (!providerConfig.provider)
        return showErrorToast({ errors: "Something went wrong" });

      // OIDC social login flow
      if (mode === "oidc" && oidcContext) {
        const res = await authService.signinByOidcEmail({
          provider: providerConfig.provider,
          clientId: oidcContext.clientId || "",
          redirectUri: oidcContext.redirectUri || "",
          scope: oidcContext.scope,
          state: oidcContext.state,
          nonce: oidcContext.nonce,
          code_challenge: oidcContext.code_challenge,
          code_challenge_method: oidcContext.code_challenge_method,
          tenantId: oidcContext.tenantId,
          provider_client_id: providerConfig.clientId,
          provider_redirect_uri: providerConfig.redirectUrl
        });

        if (res.error) return showErrorToast({ errors: res.error });
        if (!res.authorizationUrl)
          return showErrorToast({ errors: "No authorization URL provided." });

        console.log(res.authorizationUrl);
        // return;
        window.location.href = sanitizeProviderUrl(res.authorizationUrl);
        return;
      }

      // Standard embed social login flow
      const res = await oauthService.getSocialLoginEndpoint({
        provider: providerConfig.provider,
        audience: providerConfig.audience,
        sendAsResponse: true,
        nextUrl: undefined,
      });

      if (res.error) return showErrorToast({ errors: res.error });
      if (!res.providerUrl)
        return showErrorToast({ errors: "No redirect URL provided." });
      window.location.href = sanitizeProviderUrl(res.providerUrl);
    } catch (error) {
      console.error("SSO Sign-in error:", error);
      if (isErrorWithErrors(error))
        return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  }, [mode, providerConfig, oidcContext]);

  const imageSrc = useMemo(() => {
    return resolvedTheme === "light"
      ? providerConfig.imageSrc
      : providerConfig.imageSrcDark || providerConfig.imageSrc;
  }, [providerConfig, resolvedTheme]);

  return (
    <Button variant="outline" className="w-full gap-2" onClick={onClickHandler}>
      <img
        src={imageSrc}
        className="size-5 object-contain"
        alt={providerConfig.provider}
      />
      {(labelMode === "full" || (labelMode === undefined && withLabel)) && (
        <>Sign in with {providerConfig.label}</>
      )}
      {labelMode === "short" && <>{providerConfig.label}</>}
    </Button>
  );
};

