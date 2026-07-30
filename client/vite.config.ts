/// <reference types="vite/client" />
import fs from "fs";
import react from "@vitejs/plugin-react";
import path from "path";
import { defineConfig, loadEnv } from "vite";
import type { InlineConfig } from "vitest/node";

// Local dev HTTPS, driven ONLY by the machine env vars IAM_SSL_CERT /
// IAM_SSL_KEY (abs paths to an mkcert PEM cert + key). Read directly
// from process.env — NOT loadEnv, which is BLOCKS_-prefixed and would hide
// these non-prefixed names. Both set AND both files present -> HTTPS;
// otherwise warn and fall back to HTTP (returns undefined). Never throws.
// NOTE: this is consumed only by Vite's `server` block (the dev server). It is
// not referenced by `vite build`, so the built/deployed artifact is unaffected.
// `undefined` (not `false`) is the HTTP value: Vite 6 types `server.https` as
// `https.ServerOptions | undefined`, and vite.config.ts is type-checked by
// `tsc -b` (tsconfig.node.json, strict) during `npm run build`.
function resolveDevHttps(): { cert: Buffer; key: Buffer } | undefined {
  const certPath = process.env.IAM_SSL_CERT;
  const keyPath = process.env.IAM_SSL_KEY;

  if (!certPath || !keyPath) {
    console.warn(
      "[dev-https] IAM_SSL_CERT / IAM_SSL_KEY not set — serving HTTP.",
    );
    return undefined;
  }
  if (!fs.existsSync(certPath) || !fs.existsSync(keyPath)) {
    console.warn(
      `[dev-https] cert/key file missing (cert=${certPath}, key=${keyPath}) — serving HTTP.`,
    );
    return undefined;
  }
  return { cert: fs.readFileSync(certPath), key: fs.readFileSync(keyPath) };
}

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, "BLOCKS_");
  const proxyTarget = env.BLOCKS_IAM_BASE_URL;
  const logicTarget = env.BLOCKS_LOGIC_BASE_URL || "https://dev-logic.blocksdevelopers.com";
  const devHost = env.BLOCKS_DEV_HOST || true;
  const httpsConfig = resolveDevHttps();

  return {
    envPrefix: ["BLOCKS_"],
    publicDir: path.resolve(__dirname, "public"),
    plugins: [react()],
    resolve: {
      alias: {
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
    test: {
      environment: "jsdom",
      globals: true,
      setupFiles: ["./app/test-utils/vitest.setup.ts"],
      coverage: {
        all: true,
        provider: "v8",
        reporter: ["text", "json", "json-summary", "html"],
        include: ["app/**/*.{ts,tsx}"],
        exclude: [
          "app/**/*.test.*",
          "app/**/*.spec.*",
          "app/**/*.d.ts",
          "app/**/main.tsx",
          "app/**/vite-env.d.ts",
          "**/components/ui/**",
          "app/**/*.stories.*",
          "**/__generated__/**",
          "**/*.gen.*",
          "app/**/test-utils/**",
          "app/**/__mocks__/**",
        ],
      },
      alias: {
        // Stub the design-system package in tests. Its barrel eagerly imports
        // framer-motion, whose motion-utils reads `process.env.NODE_ENV` at load
        // time and crashes under jsdom. The stub also re-exports the local
        // stores so `vi.mock("@/store/...")` stays effective in hook tests.
        "@seliseblocks/genesis-os": path.resolve(
          __dirname,
          "./app/test-utils/stubs/blocks-kit.tsx",
        ),
        "@seliseblocks/genesis-os/lib": path.resolve(
          __dirname,
          "./app/test-utils/stubs/blocks-kit.tsx",
        ),
        "@seliseblocks/genesis-os/providers": path.resolve(
          __dirname,
          "./app/test-utils/stubs/blocks-kit.tsx",
        ),
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
    } as InlineConfig,
    server: {
      host: devHost,
      port: 4000,
      strictPort: true, // Fail if port is not available
      https: httpsConfig,
      allowedHosts: [
        "dev-cloud.seliseblocks.com",
        "localhost",
        ".seliseblocks.com",
        "dev-iam.blocksdevelopers.com",
      ],
      proxy: proxyTarget
        ? {
          "/logic": {
            target: logicTarget,
            changeOrigin: true,
            rewrite: (path) => path.replace(/^\/logic/, ""),
            secure: false,
          },
          "/api": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/cloudbuild": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/idp": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/identifier": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/communication": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/cloudconfiguration": {
            target: proxyTarget,
            changeOrigin: true,
            secure: false,
          },
          "/uilm": { target: proxyTarget, changeOrigin: true, secure: false },
          "/utilities": { target: proxyTarget, changeOrigin: true, secure: false },
          "/lmt": { target: proxyTarget, changeOrigin: true, secure: false },
          "/mfa": { target: proxyTarget, changeOrigin: true, secure: false },
          "/alert": { target: proxyTarget, changeOrigin: true, secure: false },
          "/blocksai-api": { target: proxyTarget, changeOrigin: true, secure: false },
          "/studio": { target: proxyTarget, changeOrigin: true, secure: false },
          "/uds": { target: proxyTarget, changeOrigin: true, secure: false },
        }
        : undefined,
    },
  };
});