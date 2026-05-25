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
      const clientId = getRuntimeEnv("BLOCKS_OS_CLIENT_ID");
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
    <div className="mx-auto flex max-w-lg flex-col items-center gap-1 text-center">
      <div className="relative mt-24 w-full overflow-hidden rounded-2xl blocks-gradient px-8 py-10 shadow-md">
        <div className="absolute -right-8 -top-8 h-40 w-40 rounded-full bg-white/5" />
        <div className="absolute -bottom-6 right-4 h-24 w-24 rounded-full bg-white/5" />
        <div className="relative">
          <h3 className="text-xl font-bold leading-tight text-white">Welcome to SELISE Blocks</h3>
          <p className="mt-1 text-sm text-white/70">Manage all your projects in one place</p>
          <p className="mt-4 text-left text-sm font-normal leading-7 text-white/80">
            Explore and manage all your projects in one place. With SELISE Blocks, building and
            scaling applications has never been easier. Start by creating a project.
          </p>
          <div className="mt-6 grid grid-cols-2 gap-4">
            <Button className="text-sm border-white/40 text-white hover:bg-white/10 hover:text-white" disabled={isLoading} onClick={openBlocksOS}>
              {isLoading ? "Redirecting…" : "Create a project"}
            </Button>
            <Button variant="ghost" className="text-sm text-white/70 hover:bg-white/10 hover:text-white" disabled>
              View documentation
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
