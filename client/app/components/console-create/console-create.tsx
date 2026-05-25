import { useState } from "react";
import { Button } from "@/components/ui-kits/button/button";
import { getRuntimeEnv } from "@/lib/runtime-env";
import { showErrorToast } from "@/hooks/use-toast";

export default function ConsoleCreateProject() {
  const [isLoading, setIsLoading] = useState(false);

  const openBlocksOS = async () => {
    if (isLoading) return;
    try {
      setIsLoading(true);
      const blocksKey = getRuntimeEnv("BLOCKS_X_BLOCKS_KEY");
      const iamBaseUrl = getRuntimeEnv("BLOCKS_IAM_BASE_URL");
      const clientId = "5225b9c1-15bc-41b0-bdc6-d3ceb180ccc5";
      const redirectUri = getRuntimeEnv("BLOCKS_OS_CALLBACK_URL");
      const initiateUrl = `${iamBaseUrl}/api/idp/initiate?x-blocks-key=${blocksKey}&clientId=${clientId}&redirectUri=${redirectUri}`;

      const headers: Record<string, string> = {};
      if (blocksKey) headers["X-Blocks-Key"] = blocksKey;

      const response = await fetch(initiateUrl, { headers });
      const data = await response.json();

      if (data.redirect_uri) {
        window.location.href = data.redirect_uri as string;
      } else {
        showErrorToast({ errors: "Failed to get authorization URL" });
        setIsLoading(false);
      }
    } catch (error) {
      console.error("Blocks OS navigation error:", error);
      showErrorToast({ errors: "Unable to open Blocks OS. Please try again." });
      setIsLoading(false);
    }
  };

  return (
    <div className="mx-auto flex max-w-2xl flex-col items-center gap-1 text-center">
      <h3 className="mt-32 text-3xl font-bold tracking-tight">Welcome to SELISE Blocks</h3>
      <div className="mt-3 max-w-lg sm:mt-5 lg:max-w-2xl">
        <p className="text-left text-base font-normal leading-7 text-high-emphasis">
          Explore and manage all your projects in one place. With SELISE Blocks, building and
          scaling applications has never been easier. Start by creating a project.
        </p>
      </div>
      <div className="mt-6 grid grid-cols-2 gap-4">
        <Button className="text-sm" disabled={isLoading} onClick={openBlocksOS}>
          {isLoading ? "Redirecting…" : "Create a project"}
        </Button>
        <Button variant="ghost" disabled>
          View documentation
        </Button>
      </div>
    </div>
  );
}
