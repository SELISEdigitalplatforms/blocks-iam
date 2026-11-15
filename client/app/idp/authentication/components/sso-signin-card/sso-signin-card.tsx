import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast } from "@/hooks/use-toast";
import { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";
import { useTheme } from "@/hooks/use-theme";
import { useCallback, useMemo } from "react";
import {
  startOidcSocialSignIn,
  type OidcSocialSignInContext,
} from "@blocks-idp/authentication/utils/start-oidc-social-sign-in";
import { tStatic, useT } from "@seliseblocks/genesis-os/i18n";

const IAM_SHARED_MODULE = "iam.shared";

type SSOSigninCardProps = {
  providerConfig: ISsoProviderConfigurationWithMeta;
  withLabel?: boolean;
  labelMode?: "full" | "short" | "icon";
  mode?: "default" | "oidc";
  oidcContext?: OidcSocialSignInContext;
  // Set while this sign-in is already under way (e.g. the page started it for the user).
  disabled?: boolean;
};

export const SSOSigninCard = ({
  providerConfig,
  withLabel = false,
  labelMode,
  mode = "default",
  oidcContext,
  disabled = false,
}: SSOSigninCardProps) => {
  const { t } = useT(IAM_SHARED_MODULE);
  const { resolvedTheme } = useTheme();

  const onClickHandler = useCallback(async () => {
    if (!providerConfig.provider)
      return showErrorToast({ errors: tStatic(IAM_SHARED_MODULE, "SSO_SIGNIN_CARD.SOMETHING_WENT_WRONG") });

    // OIDC social login flow
    if (mode === "oidc" && oidcContext) {
      await startOidcSocialSignIn({
        provider: providerConfig.provider,
        providerClientId: providerConfig.clientId,
        oidcContext,
      });
      return;
    }

    // No OIDC request on this page, so there is nothing for the provider callback to return
    // to. IAM's own /login starts one and shows these same buttons inside it.
    window.location.href = "/login";
  }, [mode, providerConfig, oidcContext]);

  const imageSrc = useMemo(() => {
    return resolvedTheme === "light"
      ? providerConfig.imageSrc
      : providerConfig.imageSrcDark || providerConfig.imageSrc;
  }, [providerConfig, resolvedTheme]);

  return (
    <Button variant="outline" className="w-full gap-2" onClick={onClickHandler} disabled={disabled}>
      <img
        src={imageSrc}
        className="size-5 object-contain"
        alt={providerConfig.provider}
      />
      {(labelMode === "full" || (labelMode === undefined && withLabel)) && (
        <>{t("SSO_SIGNIN_CARD.SIGN_IN_WITH", { provider: providerConfig.label })}</>
      )}
      {labelMode === "short" && <>{providerConfig.label}</>}
    </Button>
  );
};

