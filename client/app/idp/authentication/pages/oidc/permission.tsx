
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui-kits/card/card";
import { Link } from "react-router";
import { Fragment, useState, useEffect, useRef } from "react";
import { useOIDCContext } from "@/layouts/oidc-layout";
import { useT } from "@seliseblocks/genesis-os/i18n";

const base64UrlEncode = (bytes: Uint8Array) => {
  const binary = String.fromCharCode(...bytes);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
};

const generatePkcePair = async () => {
  const verifierBytes = crypto.getRandomValues(new Uint8Array(32));
  const verifier = base64UrlEncode(verifierBytes);
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  const challenge = base64UrlEncode(new Uint8Array(digest));

  return { verifier, challenge };
};

export const OIDCPermissionScreen = () => {
  const contextValues = useOIDCContext();
  const { userName, themeColor, state, nonce, scope, redirectUri, clientId, tenantId } = contextValues;
  const { t } = useT("iam.oidc");
  const [isSubmitting, setIsSubmitting] = useState(false);
  // The sentence holds {{terms}} and {{privacy}} markers where the two links go.
  const consentParts = t("PERMISSION.CONSENT").split(/(\{\{terms\}\}|\{\{privacy\}\})/);

  const contextRef = useRef(contextValues);

  useEffect(() => {
    contextRef.current = contextValues;
  }, [contextValues, state, scope, redirectUri, nonce, clientId, tenantId]);

  const handleDeny = () => {
    const currentContext = contextRef.current;

    if (!currentContext.redirectUri) {
      console.error("No redirect URI available");
      return;
    }

    const redirectUrl = new URL(currentContext.redirectUri);
    redirectUrl.searchParams.set("error", "access_denied");
    redirectUrl.searchParams.set("error_description", "User denied the authorization request");

    if (currentContext.state) {
      redirectUrl.searchParams.set("state", currentContext.state);
    }

    window.location.href = redirectUrl.toString();
  };

  const handleAllow = async () => {
    const currentContext = contextRef.current;

    if (!currentContext.clientId || !currentContext.redirectUri) {
      console.error("Missing client ID or redirect URI");
      return;
    }

    setIsSubmitting(true);
    try {
      // Forward the relying party's own challenge verbatim: its verifier lives on the RP. Make a
      // pair only when IAM itself is the RP (same-origin redirect), which keeps the verifier here.
      const searchParams = new URLSearchParams(globalThis.location.search);
      let challenge = searchParams.get("code_challenge");
      let challengeMethod = searchParams.get("code_challenge_method") || "S256";
      if (!challenge && new URL(currentContext.redirectUri).origin === globalThis.location.origin) {
        const pair = await generatePkcePair();
        sessionStorage.setItem("oidc-code-verifier", pair.verifier);
        challenge = pair.challenge;
        challengeMethod = "S256";
      }

      // Build authorization endpoint URL
      const authorizeUrl = new URL(`${window.location.origin}/api/oidc/authorize`);
      authorizeUrl.searchParams.set("client_id", currentContext.clientId);
      authorizeUrl.searchParams.set("response_type", "code");
      authorizeUrl.searchParams.set("redirect_uri", currentContext.redirectUri);
      authorizeUrl.searchParams.set("scope", currentContext.scope || "openid profile email");
      if (challenge) {
        authorizeUrl.searchParams.set("code_challenge", challenge);
        authorizeUrl.searchParams.set("code_challenge_method", challengeMethod);
      }

      // Add state if present
      if (currentContext.state) {
        authorizeUrl.searchParams.set("state", currentContext.state);
      }

      // Add nonce if present
      if (currentContext.nonce) {
        authorizeUrl.searchParams.set("nonce", currentContext.nonce);
      }

      // Add tenant_id if present
      if (currentContext.tenantId) {
        authorizeUrl.searchParams.set("tenant_id", currentContext.tenantId);
      }

      // Redirect to authorization endpoint
      window.location.href = authorizeUrl.toString();
    } catch (error) {
      console.error("Error during authorization:", error);
      setIsSubmitting(false);
    }
  };

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <div className="space-y-1">
          <div className="text-3xl font-semibold">{t("PERMISSION.HELLO")}</div>
          {userName && (
            <div className="wrap-break-word text-xl font-medium text-muted-foreground">{userName}</div>
          )}
        </div>
        <CardDescription className="mt-3 text-lg text-foreground">
          {t("PERMISSION.CONNECT_ACCOUNT")}
        </CardDescription>
      </CardHeader>
      <CardContent className="mt-2 flex flex-1 flex-col justify-between">
        <div className="space-y-4">
          <div className="text-left text-sm text-foreground">
            <p className="font-semibold">{t("PERMISSION.WOULD_LIKE_TO")}</p>
            <ul className="mt-2 list-inside list-disc space-y-1 pl-4">
              <li>{t("PERMISSION.AUTHENTICATE")}</li>
              <li>{t("PERMISSION.PROFILE_INFO")}</li>
              <li>{t("PERMISSION.PROJECT_DETAILS")}</li>
            </ul>
          </div>
          <div className="my-4 text-left text-sm text-foreground">
            {consentParts.map((part) => {
              if (part === "{{terms}}") {
                return (
                  <Link
                    key={part}
                    to="https://selisegroup.com/software-development-terms/"
                    className="underline"
                    style={{ color: themeColor }}
                    target="_blank"
                  >
                    {t("PERMISSION.TERMS_OF_SERVICES")}
                  </Link>
                );
              }
              if (part === "{{privacy}}") {
                return (
                  <Link
                    key={part}
                    to="https://selisegroup.com/privacy-policy/"
                    className="underline"
                    style={{ color: themeColor }}
                    target="_blank"
                  >
                    {t("PERMISSION.PRIVACY_POLICY")}
                  </Link>
                );
              }
              return <Fragment key={part}>{part}</Fragment>;
            })}
          </div>
        </div>
        <div className="mt-2 flex items-center justify-center">
          <div className="mt-4 flex w-full gap-2 text-medium-emphasis">
            <Button
              variant="outline"
              className="flex-1"
              disabled={isSubmitting}
              onClick={handleDeny}
            >
              {t("PERMISSION.DENY")}
            </Button>
            <Button
              className="flex-1"
              disabled={isSubmitting}
              onClick={handleAllow}
              style={{ backgroundColor: themeColor }}
            >
              {t("PERMISSION.ALLOW")}
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};
