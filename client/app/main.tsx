import "@seliseblocks/genesis-os/lib";
import "@/styles/globals.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router/dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v8";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import { TooltipProvider } from "./components/ui-kits/tooltip/tooltip";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import { BlocksAppLayout } from "@seliseblocks/genesis-os/providers";
import { ThemeProvider } from "./hooks/use-theme";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <ThemeProvider>
        <NuqsAdapter>
          <TooltipProvider>
            <BlocksAppLayout
              config={{
                name: "blocks-iam",
                appLogoUrl: {
                  dark: "/blocks-logos/iam_dark_mode.svg",
                  light: "/blocks-logos/iam_light_mode.svg",
                },
              }}>
              <RouterProvider router={router} />
            </BlocksAppLayout>
            <Toaster />
          </TooltipProvider>
        </NuqsAdapter>
      </ThemeProvider>
    </QueryProvider>
  </StrictMode>,
);
