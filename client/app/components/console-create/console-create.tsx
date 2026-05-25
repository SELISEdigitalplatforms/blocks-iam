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
      {/* Card with gradient header */}
      <div className="mt-24 w-full overflow-hidden rounded-2xl border border-[hsl(var(--border-default))] bg-[hsl(var(--card))] shadow-md">
        <div className="relative overflow-hidden rounded-t-2xl blocks-gradient px-6 py-7">
          <div className="absolute -right-8 -top-8 h-32 w-32 rounded-full bg-white/5" />
          <div className="absolute -bottom-6 right-4 h-20 w-20 rounded-full bg-white/5" />
          <span className="relative inline-flex items-center rounded-full bg-white/15 px-2.5 py-0.5 text-[10px] font-semibold uppercase tracking-widest text-white/80">
            Enterprise Application OS
          </span>
          <div className="relative mt-3">
            <h3 className="text-lg font-bold leading-tight text-white">Welcome to SELISE Blocks</h3>
            <p className="mt-0.5 text-xs text-white/70">Manage all your projects in one place</p>
          </div>
        </div>
        <div className="p-6">
          <p className="text-left text-base font-normal leading-7 text-high-emphasis">
            Explore and manage all your projects in one place. With SELISE Blocks, building and
            scaling applications has never been easier. Start by creating a project.
          </p>
          <div className="mt-6 grid grid-cols-2 gap-4">
            <Button variant="primary" className="text-sm" disabled={isLoading} onClick={openBlocksOS}>
              {isLoading ? "Redirecting…" : "Create a project"}
            </Button>
            <Button variant="ghost" disabled>
              View documentation
            </Button>
          </div>
        </div>
      </div>
    </div>
  );
}
