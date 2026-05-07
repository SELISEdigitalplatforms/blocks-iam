import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig, loadEnv } from "vite";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "BLOCKS_");
  const proxyTarget = env.BLOCKS_API_BASE_URL || "http://localhost:5000";
  const backendUrl = "http://localhost:5000";

  // Helper function to set origin headers for all requests
  const configureProxyOrigin = (proxy: any) => {
    proxy.on("proxyReq", (proxyReq: any) => {
      proxyReq.setHeader("origin", backendUrl);
      proxyReq.setHeader("referer", `${backendUrl}/`);
    });
  };

  return {
    envPrefix: ["BLOCKS_"],
    publicDir: path.resolve(__dirname, "app/public"),
    plugins: [react()],
    resolve: {
      alias: {
        "/assets": path.resolve(__dirname, "app/public/assets"),
        "@": path.resolve(__dirname, "./app"),
        "@blocks-idp": path.resolve(__dirname, "./app/idp"),
        "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
        "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
        "@blocks-communication": path.resolve(__dirname, "./app/cross-modules/communication"),
        "@blocks-identifier": path.resolve(__dirname, "./app/cross-modules/identifier"),
        "@blocks-localization": path.resolve(__dirname, "./app/cross-modules/localization"),
        "@blocks-utilities": path.resolve(__dirname, "./app/cross-modules/utilities"),
        "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
      },
    },
    build: {
      outDir: "../server/Api/wwwroot",
      emptyOutDir: true,
    },
    server: {
      host: "0.0.0.0", // Listen on all addresses explicitly
      port: 4000,
      allowedHosts: [
        "dev-cloud.seliseblocks.com",
        "localhost",
        "127.0.0.1",
        "idp.blocksdevelopers.com",
        "idp.seliseblocks.com",
        ".seliseblocks.com",
        ".blocksdevelopers.com",
      ],
      proxy: proxyTarget
        ? {
            "/api": {
              target: proxyTarget,
              changeOrigin: true,
              secure: false,
              configure: configureProxyOrigin,
            },
            "/cloudbuild": {
              target: proxyTarget,
              changeOrigin: true,
              secure: false,
              configure: configureProxyOrigin,
            },
            "/idp": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
              configure: configureProxyOrigin,
            },
            "/identifier": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
              configure: configureProxyOrigin,
            },
            "/communication": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
              configure: configureProxyOrigin,
            },
            "/cloudconfiguration": { 
              target: proxyTarget, 
              changeOrigin: true, 
              secure: false,
              configure: configureProxyOrigin,
            },
            "/uilm": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/utilities": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/lmt": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/mfa": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/alert": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/blocksai-api": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/studio": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
            "/uds": { target: proxyTarget, changeOrigin: true, secure: false, configure: configureProxyOrigin },
          }
        : undefined,
    },
  };
});
