import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useState } from "react";

export default function LoginPage() {
  const [isStarting, setIsStarting] = useState(false);

  const startOidcLogin = async () => {
    try {
      if (isStarting) return;
      setIsStarting(true);

      // Call backend to initiate authentication flow
      // Backend will generate OIDC params, store state, and redirect to provider
      const search = new URLSearchParams(window.location.search);
      const blocksKey =
        search.get("x-blocks-key") ||
        getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");

      const initiateUrl = new URL("/api/idp/initiate", window.location.origin);
      if (blocksKey) {
        initiateUrl.searchParams.set("x-blocks-key", blocksKey);
      }

      // Fetch the initiate endpoint - backend will return redirect URL
      const response = await fetch(initiateUrl.toString());
      const data = await response.json();

      if (data.redirect_uri) {
        // Redirect to IdP authorize endpoint
        window.location.href = data.redirect_uri;
      } else {
        showErrorToast({ errors: "Failed to get authorization URL from backend" });
        setIsStarting(false);
      }
    } catch {
      showErrorToast({ errors: "Unable to start login flow. Please try again." });
      setIsStarting(false);
    }
  };

  return (
    <Card className="flex h-full flex-col rounded border-solid border-background shadow-none md:min-w-[448px] md:border-[#95ADC4] lg:max-w-md">
      <CardHeader className="text-center">
        <CardTitle className="text-3xl">Blocks Cloud</CardTitle>
        <CardDescription className="text-xl text-foreground">Identity Provider</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-1 flex-col justify-center gap-6">
        <p className="text-center text-sm text-medium-emphasis">
          Secure sign-in for your applications using OpenID Connect.
        </p>
        <Button onClick={startOidcLogin} className="w-full" disabled={isStarting}>
          {isStarting ? "Starting..." : "Login"}
        </Button>
      </CardContent>
    </Card>
  );
}
