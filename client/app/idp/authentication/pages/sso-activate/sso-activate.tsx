

import { useState, useEffect, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { showErrorToast } from "@/hooks/use-toast";
import { useAuthStore } from "@/store/useAuthStore";
import { isErrorWithErrors } from "@/lib/error";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { Checkbox } from "@/components/ui-kits/checkbox/checkbox";
import { Button } from "@/components/ui-kits/button/button";
import {
  SOCIAL_AUTH_PROVIDERS_CONFIG,
  SSO_PROVIDERS,
} from "@blocks-idp/authentication/constants/sso-providers.constant";
import { oauthService } from "@blocks-idp/authentication/services/oauth.service";
import { authService } from "@blocks-idp/authentication/services/auth.service";
import { sanitizeProviderUrl } from "@blocks-idp/authentication/utils/sanitize-provider-url.util";
import { Link } from "react-router-dom";

type SsoActivateProps = {
  oauthParams: { code: string; username: string };
};

export const SsoActivate = ({ oauthParams }: SsoActivateProps) => {
  const { setAuthenticated } = useAuthStore();
  const [isChecked, setIsChecked] = useState(false);
  const [isPending, setIsPending] = useState(false);
  const [providerKey, setProviderKey] = useState<string | null>(null);
  const navigate = useNavigate();

  useEffect(() => {
    setProviderKey(sessionStorage.getItem("clicked_sso_provider"));
  }, []);

  const { config, providerLabel } = useMemo(() => {
    if (!providerKey) return { config: null, providerLabel: "" };

    const conf = SOCIAL_AUTH_PROVIDERS_CONFIG[providerKey as SSO_PROVIDERS];
    const label = providerKey.charAt(0).toUpperCase() + providerKey.slice(1);

    return { config: conf, providerLabel: label };
  }, [providerKey]);

  const handleUseDifferentAccount = async () => {
    const audience = sessionStorage.getItem("clicked_sso_audience");
    if (!providerKey || !audience) {
      return showErrorToast({ errors: "Something went wrong" });
    }

    try {
      const res = await oauthService.getSocialLoginEndpoint({
        provider: providerKey as SSO_PROVIDERS,
        audience: audience,
        sendAsResponse: true,
      });

      if (res.error) return showErrorToast({ errors: res.error });
      if (!res.providerUrl) return showErrorToast({ errors: "No redirect URL provided." });

      window.location.href = sanitizeProviderUrl(res.providerUrl);
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      showErrorToast({ errors: "Something went wrong" });
    }
  };

  const handleActivate = async () => {
    if (!oauthParams?.code) return showErrorToast({ errors: "Code is missing" });
    setIsPending(true);
    try {
      await authService.verifySsoConsent(oauthParams.code);

      sessionStorage.removeItem("clicked_sso_provider");
      sessionStorage.removeItem("clicked_sso_audience");

      setAuthenticated();
      navigate("/console");
    } catch (error) {
      if (isErrorWithErrors(error)) {
        showErrorToast({ errors: error.errors });
      } else {
        showErrorToast({ errors: "Something went wrong" });
      }
    } finally {
      setIsPending(false);
    }
  };

  return (
    <Card className="w-full rounded border-solid border-background shadow-none md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl leading-9">Blocks Cloud</CardTitle>
      </CardHeader>
      <CardContent>
        {config && (
          <>
            <div className="flex items-start gap-3 py-4 text-base font-medium">
              <div className="mt-1">
                <img src={config.imageSrc} width={16} height={16} alt={providerLabel} />
              </div>
              <span className="text-medium-emphasis">
                Signing in with <span className="font-bold">{providerLabel}</span> account (
                <span className="font-bold underline">{oauthParams.username}</span>)
              </span>
            </div>
            <div
              onClick={handleUseDifferentAccount}
              className="mb-8 cursor-pointer text-base font-semibold text-primary hover:underline"
            >
              Use a different {providerLabel} account
            </div>
          </>
        )}

        <div className="mt-2 flex justify-start gap-2 text-sm text-foreground">
          <Checkbox
            id="terms"
            checked={isChecked}
            onCheckedChange={(checked) => setIsChecked(!!checked)}
            className="mt-1 shrink-0"
          />
          <label
            htmlFor="terms"
            className="cursor-pointer text-sm font-medium peer-disabled:cursor-not-allowed peer-disabled:opacity-70"
          >
            I agree to the{" "}
            <Link
              to="https://selisegroup.com/software-development-terms/"
              className="text-primary underline"
              target="_blank"
            >
              Terms of Services{" "}
            </Link>
            and acknowledge that I have read the{" "}
            <Link
              to="https://selisegroup.com/privacy-policy/"
              className="text-primary underline"
              target="_blank"
            >
              Privacy policy.
            </Link>
          </label>
        </div>
        <Button
          onClick={handleActivate}
          className="mt-6 w-full rounded"
          disabled={isPending || !isChecked}
        >
          Continue
        </Button>
      </CardContent>
    </Card>
  );
};
