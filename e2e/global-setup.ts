import fs from "fs";
import path from "path";

/**
 * Point the locally-served Blocks IAM at itself (:5001), not the remote host.
 *
 * The built index.html carries runtime config in `window.__BLOCKS_ENV__`. The
 * .NET host bakes `BLOCKS_IAM_BASE_URL` from the Mongo secret, which is the
 * deployed host WITHOUT a port (https://dev-iam.blocksdevelopers.com).
 * When we run IAM locally on :5001, the SPA would then send its API calls to
 * the remote host, so the console shows no local data. This patches the
 * served index.html so BLOCKS_IAM_BASE_URL === E2E_BASE_URL.
 *
 * Idempotent and order-independent: it rewrites the concrete value (or the
 * `__BLOCKS_IAM_BASE_URL__` placeholder), so it holds whether it runs before or
 * after the host's own startup replacement. Because the command in
 * playwright.config.ts is `run.sh -b` (no FE rebuild), nothing overwrites it.
 */
export default function globalSetup() {
  const baseURL = process.env.E2E_BASE_URL?.replace(/\/$/, "");
  if (!baseURL) return; // playwright.config.ts already throws when unset

  const indexHtml = path.resolve(__dirname, "../server/Api/wwwroot/index.html");
  if (!fs.existsSync(indexHtml)) {
    const isLocalTarget = /localhost|127\.0\.0\.1|:5000|:5001/.test(baseURL);
    if (isLocalTarget) {
      console.warn(
        `[e2e] index.html not found at ${indexHtml} — skipping BLOCKS_IAM_BASE_URL patch. ` +
          `Build the FE first (cd client && npm run build, or run.sh -a).`,
      );
    }
    return;
  }

  const original = fs.readFileSync(indexHtml, "utf8");
  const patched = original.replace(/(BLOCKS_IAM_BASE_URL:\s*")([^"]*)(")/g, `$1${baseURL}$3`);

  if (patched === original) {
    console.log(`[e2e] BLOCKS_IAM_BASE_URL already "${baseURL}" — no patch needed.`);
    return;
  }

  fs.writeFileSync(indexHtml, patched);
  console.log(`[e2e] Patched BLOCKS_IAM_BASE_URL -> "${baseURL}" in served index.html.`);
}
