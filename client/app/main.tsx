import "@seliseblocks/genesis-os/lib";
import "@/styles/globals.css";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router/dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v8";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import { TooltipProvider } from "./components/ui-kits/tooltip/tooltip";
import QueryProvider from "./providers/query-provider";
import { SERVICE_NAME } from "@/constants/service.constant";
import { router } from "./router";
import { BlocksAppLayout } from "@seliseblocks/genesis-os/providers";
import { ThemeProvider } from "./hooks/use-theme";
import { RollbarProvider } from "@seliseblocks/genesis-os/observability";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <RollbarProvider service={SERVICE_NAME}>
      <QueryProvider>
        <ThemeProvider>
          <NuqsAdapter>
            <TooltipProvider>
              <BlocksAppLayout
                config={{
                  name: SERVICE_NAME,
                  appLogoUrl: {
                    dark: "/blocks-logos/iam_dark_mode.svg",
                    light: "/blocks-logos/iam_light_mode.svg",
                  },
                }}
              >
                <RouterProvider router={router} />
              </BlocksAppLayout>
              <Toaster />
            </TooltipProvider>
          </NuqsAdapter>
        </ThemeProvider>
      </QueryProvider>
    </RollbarProvider>
  </StrictMode>,
);
