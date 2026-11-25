import { StrictMode, type ReactNode } from "react";
import { act, fireEvent, render, screen } from "@testing-library/react";
import { createMemoryRouter, Outlet, RouterProvider } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => {
  // A fresh fake per test: the transform is installed once per Rollbar instance.
  const makeRollbar = () => {
    const options: Record<string, unknown> = {};
    return {
      options,
      configure: vi.fn((next: Record<string, unknown>) => Object.assign(options, next)),
      error: vi.fn(),
    };
  };
  return { makeRollbar, rollbar: makeRollbar(), getRollbar: vi.fn() };
});

vi.mock("@seliseblocks/genesis-os/observability", () => ({
  createHttpFailureReporter: () => () => undefined,
  getRollbar: (...args: unknown[]) => {
    h.getRollbar(...args);
    return h.rollbar;
  },
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", async () => {
  const { OIDC_UI_TEMPLATE_FIXTURE } = await import(
    "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture"
  );
  const data = { captcha: null, template: OIDC_UI_TEMPLATE_FIXTURE };
  return { useOidcUiConfig: () => ({ data, isLoading: false }) };
});

import { OidcRouteErrorScreen } from "./oidc-route-error-screen";
import { OIDC_ROUTE_ERROR_SOURCE, reportOidcRouteError, stripQueryStrings } from "./oidc-route-error-report";
import { router as appRouter } from "@/router";

const QUERY = "?code=secret-code&clientId=client-1&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb&lang=en";
const PAGE_URL = `https://iam.example.com/oidc/activate/tenant-1${QUERY}`;

const Thrower = (): ReactNode => {
  throw new Error("render failed");
};

const renderRoute = (strict = false) => {
  const router = createMemoryRouter(
    [
      {
        path: "/oidc",
        element: <Outlet />,
        errorElement: <OidcRouteErrorScreen />,
        children: [{ path: "activate/:tenantId", element: <Thrower /> }],
      },
    ],
    { initialEntries: [`/oidc/activate/tenant-1${QUERY}`] },
  );
  const app = <RouterProvider router={router} />;
  return render(strict ? <StrictMode>{app}</StrictMode> : app);
};

const realLocation = window.location;
let reload: ReturnType<typeof vi.fn>;

beforeEach(() => {
  vi.clearAllMocks();
  h.rollbar = h.makeRollbar();
  vi.spyOn(console, "error").mockImplementation(() => undefined);
  vi.spyOn(console, "warn").mockImplementation(() => undefined);
  reload = vi.fn();
  const url = new URL(PAGE_URL);
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      href: url.href,
      origin: url.origin,
      protocol: url.protocol,
      host: url.host,
      hostname: url.hostname,
      pathname: url.pathname,
      search: url.search,
      hash: url.hash,
      reload,
      assign: vi.fn(),
      replace: vi.fn(),
    },
  });
});

afterEach(() => {
  Object.defineProperty(window, "location", { configurable: true, value: realLocation });
  vi.restoreAllMocks();
});

describe("OidcRouteErrorScreen (blocks-iam#682)", () => {
  it("shows the recovery screen instead of a blank page when an OIDC route throws (C1)", async () => {
    renderRoute();

    expect(await screen.findByRole("button", { name: "Try again" })).toBeInTheDocument();
    // The shell's heading renders one span per word.
    expect(screen.getByRole("heading", { level: 1 })).toHaveTextContent(/^Something\s*went\s*wrong$/);
    expect(screen.getByRole("link", { name: "Back to login" })).toBeInTheDocument();
  });

  it("reports the error once, with the route path and no query string values (C2)", async () => {
    renderRoute(true);
    await screen.findByRole("button", { name: "Try again" });

    expect(h.rollbar.error).toHaveBeenCalledTimes(1);
    const [reported, custom] = h.rollbar.error.mock.calls[0];
    expect(reported).toBeInstanceOf(Error);
    expect((reported as Error).message).toBe("render failed");
    expect(custom).toEqual({ source: OIDC_ROUTE_ERROR_SOURCE, route: "/oidc/activate/tenant-1" });
    expect(JSON.stringify(custom)).not.toMatch(/secret-code|redirect_uri|app\.example\.com/);
    expect(h.getRollbar).toHaveBeenCalledWith({ service: "blocks-iam" });
  });

  it("strips every query string from the item Rollbar builds around the report (C2)", async () => {
    renderRoute();
    await screen.findByRole("button", { name: "Try again" });
    const transform = h.rollbar.options.transform as (data: unknown, item: unknown) => void;
    const item = {
      custom: { source: OIDC_ROUTE_ERROR_SOURCE, route: "/oidc/activate/tenant-1" },
      request: { url: PAGE_URL, query_string: QUERY },
      body: {
        telemetry: [
          { type: "navigation", body: { from: PAGE_URL, to: `${PAGE_URL}#top` } },
          { type: "network", body: { url: "https://iam.example.com/api/idp/oidc-ui-config?tenant_id=tenant-1" } },
          { type: "log", body: { message: "plain" } },
        ],
      },
    };

    transform(item, {});

    expect(item.request).toEqual({ url: "https://iam.example.com/oidc/activate/tenant-1" });
    expect(JSON.stringify(item)).not.toMatch(/secret-code|redirect_uri|client-1|tenant_id=/);
    expect(item.body.telemetry[2].body).toEqual({ message: "plain" });
  });

  it("leaves items it did not send untouched and keeps an earlier transform", async () => {
    const earlier = vi.fn();
    h.rollbar.options.transform = earlier;
    renderRoute();
    await screen.findByRole("button", { name: "Try again" });
    const transform = h.rollbar.options.transform as (data: unknown, item: unknown) => void;
    const other = { custom: { source: "react-query" }, request: { url: PAGE_URL, query_string: QUERY } };

    transform(other, {});

    expect(other.request).toEqual({ url: PAGE_URL, query_string: QUERY });
    expect(earlier).toHaveBeenCalledWith(other, {});
  });

  it("reloads the same URL, query included, on Try again (C3)", async () => {
    renderRoute();
    fireEvent.click(await screen.findByRole("button", { name: "Try again" }));

    expect(reload).toHaveBeenCalledOnce();
    expect(window.location.href).toBe(PAGE_URL);
  });

  it("sends Back to login to the originating application when the link carried its redirect_uri", async () => {
    renderRoute();
    const link = await screen.findByRole("link", { name: "Back to login" });

    expect(link).toHaveAttribute("href", "https://app.example.com");
  });

  it("puts the side panel in its failed state with the title", async () => {
    const { container } = renderRoute();
    await screen.findByRole("button", { name: "Try again" });

    await act(async () => {});
    expect(container.querySelector("[data-anim-phase]")).toHaveAttribute("data-anim-phase", "failed");
  });
});

describe("reportOidcRouteError", () => {
  it("describes a thrown value that is not an Error", () => {
    reportOidcRouteError({ status: 404, statusText: "Not Found" }, "/oidc/missing?code=x");
    reportOidcRouteError("plain text", "/oidc/login");

    expect((h.rollbar.error.mock.calls[0][0] as Error).message).toBe("404 Not Found");
    expect(h.rollbar.error.mock.calls[0][1]).toEqual({ source: OIDC_ROUTE_ERROR_SOURCE, route: "/oidc/missing" });
    expect((h.rollbar.error.mock.calls[1][0] as Error).message).toBe("plain text");
  });
});

describe("stripQueryStrings", () => {
  it("keeps values that are not URLs with a query and tolerates missing parts", () => {
    const data = { request: { url: 42 }, body: { telemetry: [{}, { body: { url: "/plain/path" } }] } };
    stripQueryStrings(data as never);
    expect(data).toEqual({ request: { url: 42 }, body: { telemetry: [{}, { body: { url: "/plain/path" } }] } });
    expect(() => stripQueryStrings({})).not.toThrow();
  });
});

describe("app router", () => {
  it("puts the recovery screen on the OIDC routes", () => {
    const oidc = appRouter.routes[0].children?.find((route) => route.path === "/oidc");
    expect(oidc?.errorElement).toBeTruthy();
    expect(oidc?.children?.some((route) => route.path === "activate/:tenantId")).toBe(true);
  });
});
