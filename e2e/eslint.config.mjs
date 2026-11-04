// Flat-config equivalent of the org's e2e .eslintrc.cjs (eslint:recommended,
// @typescript-eslint/recommended, prettier); ESLint 10 no longer reads .eslintrc files.
import js from "@eslint/js";
import tsPlugin from "@typescript-eslint/eslint-plugin";
import prettier from "eslint-config-prettier";

export default [
  {
    ignores: ["node_modules/", "test-results/", "playwright-report/", "fixtures/*.json"],
  },
  js.configs.recommended,
  ...tsPlugin.configs["flat/recommended"],
  {
    // Same convention as the client: a leading underscore marks an intentionally unused
    // name, e.g. a property dropped with rest destructuring.
    rules: {
      "@typescript-eslint/no-unused-vars": [
        "error",
        { argsIgnorePattern: "^_", varsIgnorePattern: "^_" },
      ],
    },
  },
  prettier,
];
