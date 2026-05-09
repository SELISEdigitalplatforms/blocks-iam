import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { showErrorToast } from "@/hooks/use-toast";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { useState } from "react";

export default function LoginPage() {
  const [isStarting, setIsStarting] = useState(false);

  const startLogin = async () => {
    try {
      if (isStarting) return;
      setIsStarting(true);

      const search = new URLSearchParams(window.location.search);
      const blocksKey = search.get("x-blocks-key") || getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
      const apiBaseUrl = getRuntimeEnv("BLOCKS_API_BASE_URL") || "http://localhost:5000";

      const initiateUrl = new URL("/api/idp/initiate", apiBaseUrl);
      if (blocksKey) initiateUrl.searchParams.set("x-blocks-key", blocksKey);

      const headers: Record<string, string> = {};
      if (blocksKey) headers["X-Blocks-Key"] = blocksKey;

      const response = await fetch(initiateUrl.toString(), { headers });
      const data = await response.json();

      if (data.redirect_uri) {
        window.location.href = data.redirect_uri;
      } else {
        showErrorToast({ errors: "Failed to get authorization URL" });
        setIsStarting(false);
      }
    } catch {
      showErrorToast({ errors: "Unable to start login. Please try again." });
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
        <Button onClick={startLogin} className="w-full" disabled={isStarting}>
          {isStarting ? "Starting..." : "Login"}
        </Button>
      </CardContent>
    </Card>
  );
}

