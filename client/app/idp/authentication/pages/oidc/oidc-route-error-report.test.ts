import { afterEach, describe, expect, it, vi } from "vitest";
// The browser build of rollbar.js, the one the app bundles. The package's `node` export is the
// server notifier, which adds no page URL or telemetry and so could not show the scrub working.
import BrowserRollbar from "../../../../../node_modules/rollbar/src/browser/rollbar.js";

const h = vi.hoisted(() => ({ instance: undefined as unknown }));
vi.mock("@seliseblocks/genesis-os/observability", () => ({ getRollbar: () => h.instance }));

import { reportOidcRouteError } from "./oidc-route-error-report";

const PAGE = "/oidc/activate/tenant-1?code=secret-code&clientId=client-1&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb";

afterEach(() => {
  window.history.replaceState(null, "", "/");
});

describe("reportOidcRouteError with the real browser Rollbar pipeline (C2)", () => {
  it("sends the item with no query string in the page URL or the telemetry trail", async () => {
    window.history.replaceState(null, "", PAGE);
    const rollbar = new BrowserRollbar({
      accessToken: "test-token",
      enabled: true,
      transmit: false,
      captureUncaught: false,
      captureUnhandledRejections: false,
    });
    // An earlier transform sees the item after the scrub, as Rollbar's own later steps do.
    const sent = new Promise<Record<string, unknown>>((resolve) => {
      rollbar.configure({ transform: (data: Record<string, unknown>) => resolve(data) });
    });
    rollbar.captureEvent({ url: `https://iam.example.com${PAGE}` }, "info");
    h.instance = rollbar;

    reportOidcRouteError(new Error("render failed"), "/oidc/activate/tenant-1");
    const data = await sent;

    expect(data.request).toEqual(expect.objectContaining({ url: "http://localhost:3000/oidc/activate/tenant-1" }));
    expect(data.request).not.toHaveProperty("query_string");
    expect(data.custom).toEqual(expect.objectContaining({ source: "oidc-route-error", route: "/oidc/activate/tenant-1" }));
    expect(JSON.stringify(data)).not.toMatch(/secret-code|redirect_uri|client-1/);
  });
});
