
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardDescription, CardHeader } from "@/components/ui-kits/card/card";
import { Link } from "react-router-dom";
import { useState, useEffect, useRef } from "react";
import { useOIDCContext } from "@/layouts/oidc-layout";

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
  const [isSubmitting, setIsSubmitting] = useState(false);

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
      // Generate PKCE pair
      const { verifier, challenge } = await generatePkcePair();
      sessionStorage.setItem("oidc-code-verifier", verifier);

      // Build authorization endpoint URL
      const authorizeUrl = new URL(`${window.location.origin}/api/oidc/authorize`);
      authorizeUrl.searchParams.set("client_id", currentContext.clientId);
      authorizeUrl.searchParams.set("response_type", "code");
      authorizeUrl.searchParams.set("redirect_uri", currentContext.redirectUri);
      authorizeUrl.searchParams.set("scope", currentContext.scope || "openid profile email");
      authorizeUrl.searchParams.set("code_challenge", challenge);
      authorizeUrl.searchParams.set("code_challenge_method", "S256");

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
          <div className="text-3xl font-semibold">Hello</div>
          {userName && (
            <div className="break-words text-xl font-medium text-muted-foreground">{userName}</div>
          )}
        </div>
        <CardDescription className="mt-3 text-lg text-foreground">
          You&apos;re about to connect your Blocks Account to Blocks Cloud
        </CardDescription>
      </CardHeader>
      <CardContent className="mt-2 flex flex-1 flex-col justify-between">
        <div className="space-y-4">
          <div className="text-left text-sm text-foreground">
            <p className="font-semibold">This portal would like to:</p>
            <ul className="mt-2 list-inside list-disc space-y-1 pl-4">
              <li>Authenticate you with your Blocks Account</li>
              <li>Access your&apos; basic profile information</li>
              <li>Access your project details</li>
            </ul>
          </div>
          <div className="my-4 text-left text-sm text-foreground">
            By clicking Allow, you permit Blocks Cloud to use your information in accordance with
            its{" "}
            <Link
              to="https://selisegroup.com/software-development-terms/"
              className="underline"
              style={{ color: themeColor }}
              target="_blank"
            >
              Terms of Services{" "}
            </Link>
            and{" "}
            <Link
              to="https://selisegroup.com/privacy-policy/"
              className="underline"
              style={{ color: themeColor }}
              target="_blank"
            >
              Privacy policy.
            </Link>
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
              Deny
            </Button>
            <Button
              className="flex-1"
              disabled={isSubmitting}
              onClick={handleAllow}
              style={{ backgroundColor: themeColor }}
            >
              Allow
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};
