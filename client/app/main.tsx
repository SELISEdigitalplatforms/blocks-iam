import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { RouterProvider } from "react-router-dom";
import { NuqsAdapter } from "nuqs/adapters/react-router/v6";
import { Toaster } from "./components/ui-kits/toaster/toaster";
import QueryProvider from "./providers/query-provider";
import { router } from "./router";
import "./styles/globals.css";
import { BlocksAppLayout } from "@seliseblocks/blocks-kit";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <QueryProvider>
      <NuqsAdapter>
        
         <BlocksAppLayout
            config={{
              userBaseUrlKey: "BLOCKS_IAM_BASE_URL",
              projectBaseUrlKey: "BLOCKS_LOGIC_BASE_URL",
               appLogoUrl:{
                dark:"/iam_dark_mode.svg",
                light:"/iam_light_mode.svg"
              }
            }}
          >
            <RouterProvider router={router} />
          </BlocksAppLayout>
        <Toaster />
      </NuqsAdapter>
    </QueryProvider>
  </StrictMode>,
);
