// Multi-environment test config: DOM-free suites run on `node`, everything
// else on `jsdom`. Verified green (2604 passing) at ~118s vs ~145s single-env.
//
// Not yet wired into `npm test` — run with:
//   npx vitest run --config vitest.projects.config.ts
// To adopt permanently, move the `test.projects` block into vite.config.ts.
import path from "path";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";
import { globSync } from "tinyglobby";

const alias = {
  "@seliseblocks/genesis-os/lib": path.resolve(__dirname, "./app/test-utils/stubs/blocks-kit.tsx"),
  "@seliseblocks/genesis-os/providers": path.resolve(__dirname, "./app/test-utils/stubs/blocks-kit.tsx"),
  "@seliseblocks/genesis-os/hooks": path.resolve(__dirname, "./app/test-utils/stubs/blocks-kit.tsx"),
  "@seliseblocks/genesis-os": path.resolve(__dirname, "./app/test-utils/stubs/blocks-kit.tsx"),
  "@": path.resolve(__dirname, "./app"),
  "@blocks-idp": path.resolve(__dirname, "./app/idp"),
  "@blocks-lmt": path.resolve(__dirname, "./app/cross-modules/lmt"),
  "@blocks-storage": path.resolve(__dirname, "./app/cross-modules/storage"),
  "@blocks-communication": path.resolve(__dirname, "./app/cross-modules/communication"),
  "@blocks-identifier": path.resolve(__dirname, "./app/cross-modules/identifier"),
  "@blocks-localization": path.resolve(__dirname, "./app/cross-modules/localization"),
  "@blocks-utilities": path.resolve(__dirname, "./app/cross-modules/utilities"),
  "@blocks-ai": path.resolve(__dirname, "./app/cross-modules/ai"),
};

// DOM-free areas: pure request/mapping/format logic.
// .ts only — a .tsx test renders JSX and therefore needs jsdom.
const NODE_GLOBS = [
  "app/**/services/**/*.test.ts",
  "app/**/utils/**/*.test.ts",
  "app/**/mappers/**/*.test.ts",
  "app/**/*.service.test.ts",
  "app/**/*.utils.test.ts",
  "app/**/*.util.test.ts",
];

// These sit in DOM-free folders but touch `window` directly, so they stay on jsdom.
const NEEDS_DOM = [
  "app/cross-modules/devops/services/github-info.service.test.ts",
  "app/cross-modules/devops/services/providers.service.test.ts",
  "app/cross-modules/storage/services/storage-file.service.test.ts",
  "app/cross-modules/storage/services/storage.service.test.ts",
  "app/idp/authentication/utils/oidc-navigation.util.test.ts",
  "app/idp/authentication/utils/oidc-utils.test.ts",
  "app/notifications/services/notification-client.service.test.ts",
  "app/notifications/services/notification.service.test.ts",
];

// Resolve the node set once, so the jsdom project can exclude exactly those
// files. Glob `exclude` beats `include`, so NEEDS_DOM entries must be removed
// from the node list rather than re-added to jsdom's include.
const NODE_FILES = globSync(NODE_GLOBS, { cwd: __dirname })
  .map((f) => f.split("\\").join("/"))
  .filter((f) => !NEEDS_DOM.includes(f));

const shared = {
  globals: true,
  setupFiles: ["./app/test-utils/vitest.setup.ts"],
  alias,
};

export default defineConfig({
  plugins: [react()],
  resolve: { alias },
  test: {
    projects: [
      {
        plugins: [react()],
        resolve: { alias },
        test: {
          ...shared,
          name: "node",
          environment: "node",
          include: NODE_FILES,
        },
      },
      {
        plugins: [react()],
        resolve: { alias },
        test: {
          ...shared,
          name: "jsdom",
          environment: "jsdom",
          include: ["app/**/*.test.{ts,tsx}"],
          exclude: ["**/node_modules/**", ...NODE_FILES],
        },
      },
    ],
  },
});
