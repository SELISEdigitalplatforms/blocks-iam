import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v6";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import "./styles/globals.css";
import { BlocksAppLayout, TooltipProvider } from "@seliseblocks/blocks-kit";
import { ThemeProvider } from "./hooks/use-theme";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <ThemeProvider>

      <NuqsAdapter>
         <TooltipProvider delayDuration={0}>
         <BlocksAppLayout
            config={{
              name: "blocks-iam",
              appLogoUrl:{
                dark:"/blocks-logos/iam_dark_mode.svg",
                light:"/blocks-logos/iam_light_mode.svg"
              }
            }}
            >
            <RouterProvider router={router} />
          </BlocksAppLayout>
        <Toaster />
        </TooltipProvider>
      </NuqsAdapter>
              </ThemeProvider>
    </QueryProvider>
  </StrictMode>,
);
