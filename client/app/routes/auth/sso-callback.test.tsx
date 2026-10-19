import type { ReactNode } from "react";
import { render, screen, waitFor } from "@testing-library/react";
import { Routes, Route, MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  setAuthenticated: vi.fn(),
  getSelfBaseUrl: vi.fn(),
  oidcUiConfig: undefined as unknown,
  uiConfigTenant: undefined as string | undefined,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated }),
}));
vi.mock("@/lib/runtime-env", () => ({ getSelfBaseUrl: h.getSelfBaseUrl }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  getCurrentOIDCParams: () => new URLSearchParams(),
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY: "oidc-device-return-url",
  OIDC_APP_HANDOFF_STORAGE_KEY: "oidc_app_handoff",
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: (tenantId?: string) => {
    h.uiConfigTenant = tenantId;
    return { data: h.oidcUiConfig };
  },
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({ children, ...props }: { children: ReactNode } & Record<string, unknown>) => {
    h.shellProps = props;
    return <div data-testid="oidc-shell">{children}</div>;
  },
  OidcFooter: () => null,
}));

import SSOCallbackPage from "./sso-callback";
import { OIDC_LOGIN_PANEL } from "@blocks-idp/authentication/pages/oidc/oidc-panel-config";
import { DEFAULT_OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const renderAt = (entry: string) =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/sso/:tenantId/callback" element={<SSOCallbackPage />} />
      </Routes>
    </MemoryRouter>,
  );

const originalLocation = window.location;
beforeEach(() => {
  vi.clearAllMocks();
  h.oidcUiConfig = undefined;
  h.uiConfigTenant = undefined;
  h.shellProps = null;
  h.getSelfBaseUrl.mockReturnValue("https://iam.example.com");
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { ...originalLocation, href: "http://localhost/" },
  });
});
afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
  sessionStorage.clear();
  vi.unstubAllGlobals();
});

describe("SSOCallbackPage", () => {
  it("builds the backend callback URL with code, state and tenant, then redirects", () => {
    const { container } = renderAt("/sso/tenant-1/callback?code=c1&state=s1");
    expect(container.querySelector("img")).not.toBeNull();
    expect(window.location.href).toContain("/api/oidc/callback");
    expect(window.location.href).toContain("code=c1");
    expect(window.location.href).toContain("state=s1");
    expect(window.location.href).toContain("tenant_id=tenant-1");
  });

  it("issues no fetch to the callback when the app marker is absent (C13)", () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);
    renderAt("/sso/tenant-1/callback?code=c1&state=s1");
    expect(fetchMock).not.toHaveBeenCalled();
    expect(window.location.href).toContain("/api/oidc/callback?code=c1");
  });

  it("renders nothing when code and state are missing", () => {
    const { container } = renderAt("/sso/tenant-1/callback");
    expect(container.querySelector("img")).toBeNull();
  });

  it("forwards a provider refusal instead of calling back with no code", () => {
    // Cancelling on the provider consent screen sends `error` and no `code`. Dropping it here
    // would leave the backend answering a codeless callback, which the browser renders as raw
    // JSON; forwarding it lets the backend resolve the state and show the user a real page.
    renderAt("/sso/tenant-1/callback?error=access_denied&error_description=User%20said%20no&state=s1");

    expect(window.location.href).toContain("/api/oidc/callback");
    expect(window.location.href).toContain("error=access_denied");
    expect(window.location.href).toContain("error_description=User+said+no");
    expect(window.location.href).toContain("state=s1");
    expect(window.location.href).not.toContain("code=");
  });

  it("keeps the spinner up while a provider refusal is being forwarded", () => {
    const { container } = renderAt("/sso/tenant-1/callback?error=access_denied&state=s1");
    expect(container.querySelector("img")).not.toBeNull();
  });

  describe("device flow (RFC 8628)", () => {
    const deviceReturnUrl = "https://iam.example.com/oidc/device/entry?client_id=dev1";

    beforeEach(() => {
      sessionStorage.setItem("oidc-device-return-url", deviceReturnUrl);
    });

    it("fetches the callback instead of navigating, then redirects to the stashed returnUrl", async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: () => Promise.resolve({ success: true }),
      });
      vi.stubGlobal("fetch", fetchMock);

      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() => expect(fetchMock).toHaveBeenCalled());
      expect(fetchMock.mock.calls[0][0]).toContain("/api/oidc/callback");
      // Without this the backend treats the call as a browser navigation and answers with a
      // redirect, which fetch follows to an HTML 200 -- ok, no body, failure never surfaced.
      expect(fetchMock.mock.calls[0][1]).toMatchObject({
        headers: { Accept: "application/json" },
      });
      await waitFor(() =>
        expect(window.location.href).toBe(deviceReturnUrl),
      );
      expect(sessionStorage.getItem("oidc-device-return-url")).toBeNull();
    });

    it("falls back to the stashed returnUrl when the callback fails", async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: false,
        json: () => Promise.resolve({ error_description: "boom" }),
      });
      vi.stubGlobal("fetch", fetchMock);

      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() =>
        expect(window.location.href).toBe(deviceReturnUrl),
      );
    });

    it("asks for JSON when forwarding a provider refusal too", async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: false,
        json: () => Promise.resolve({ error_description: "User said no" }),
      });
      vi.stubGlobal("fetch", fetchMock);

      renderAt("/sso/tenant-1/callback?error=access_denied&state=s1");

      await waitFor(() => expect(fetchMock).toHaveBeenCalled());
      expect(fetchMock.mock.calls[0][0]).toContain("error=access_denied");
      expect(fetchMock.mock.calls[0][1]).toMatchObject({
        headers: { Accept: "application/json" },
      });
    });
  });

  describe("native-app hand-off (JSON callback)", () => {
    const APP_URL = "com.klaxai.app://login/callback?code=abc&state=xyz";
    const ERROR_BASE = "https://iam.example.com/oidc/error";
    let assignments: string[];

    beforeEach(() => {
      assignments = [];
      sessionStorage.setItem("oidc_app_handoff", "1");
      Object.defineProperty(window, "location", {
        configurable: true,
        writable: true,
        value: {
          origin: "https://iam.example.com",
          get href() {
            return assignments[assignments.length - 1] ?? "https://iam.example.com/oidc/x";
          },
          set href(value: string) {
            assignments.push(value);
          },
        },
      });
    });

    const respond = (ok: boolean, body: unknown, status = ok ? 200 : 400) => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok,
        status,
        json: () => (body === undefined ? Promise.reject(new Error("no json")) : Promise.resolve(body)),
      });
      vi.stubGlobal("fetch", fetchMock);
      return fetchMock;
    };

    it("removes the marker and asks for JSON instead of navigating (H10)", async () => {
      const fetchMock = respond(true, { redirect_uri: APP_URL });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));
      const [url, init] = fetchMock.mock.calls[0];
      expect(url).toContain("https://iam.example.com/api/oidc/callback?code=c1&state=s1&tenant_id=tenant-1");
      expect(init).toMatchObject({ headers: { Accept: "application/json" } });
      expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
    });

    it("opens the app once and shows the hand-off inside the login shell (H10)", async () => {
      h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
      respond(true, { redirect_uri: APP_URL });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      const link = await screen.findByRole("link", { name: "Return to the app" });
      expect(link).toHaveAttribute("href", APP_URL);
      expect(screen.getByTestId("oidc-shell")).toContainElement(link);
      expect(assignments).toEqual([APP_URL]);
      expect(h.uiConfigTenant).toBe("tenant-1");
      expect(h.shellProps).toMatchObject({
        panelConfig: OIDC_LOGIN_PANEL,
        heading: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE.pages.login.heading,
        successTitle: "Access Granted",
        successSubtitle: "Redirecting to your application…",
        brandName: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE.branding.brandName,
      });
    });

    it("shows the full-screen loader until the template loads", async () => {
      respond(true, { redirect_uri: APP_URL });
      const { container } = renderAt("/sso/tenant-1/callback?code=c1&state=s1");
      await waitFor(() => expect(container.querySelector(".oidc-scifi-root")).not.toBeNull());
      expect(screen.queryByTestId("oidc-shell")).toBeNull();
      expect(screen.queryByRole("link", { name: "Return to the app" })).toBeNull();
    });

    it("follows a web redirect_uri once with no hand-off", async () => {
      respond(true, { redirect_uri: "https://app.example.com/cb?code=1&state=s1" });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() => expect(assignments).toEqual(["https://app.example.com/cb?code=1&state=s1"]));
      expect(screen.queryByRole("link", { name: "Return to the app" })).toBeNull();
    });

    it("sends a non-2xx answer to the error page with its values URL-encoded (C12)", async () => {
      respond(false, { error: "access_denied", error_description: "No account & no sign-up" });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() =>
        expect(assignments).toEqual([
          `${ERROR_BASE}?error=access_denied&error_description=No%20account%20%26%20no%20sign-up&tenant_id=tenant-1`,
        ]),
      );
    });

    it("uses the default error values for a non-JSON failure (C12)", async () => {
      respond(false, undefined, 500);
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() =>
        expect(assignments).toEqual([
          `${ERROR_BASE}?error=server_error&error_description=Sign-in%20could%20not%20be%20completed.&tenant_id=tenant-1`,
        ]),
      );
    });

    it("treats a 200 without redirect_uri as an error (C12)", async () => {
      respond(true, { success: true });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() =>
        expect(assignments).toEqual([
          `${ERROR_BASE}?error=server_error&error_description=Sign-in%20could%20not%20be%20completed.&tenant_id=tenant-1`,
        ]),
      );
    });

    it("sends a network failure to the error page (C12)", async () => {
      vi.stubGlobal("fetch", vi.fn().mockRejectedValue(new TypeError("Failed to fetch")));
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() =>
        expect(assignments).toEqual([
          `${ERROR_BASE}?error=server_error&error_description=Sign-in%20could%20not%20be%20completed.&tenant_id=tenant-1`,
        ]),
      );
    });

    it("never follows an unsafe redirect_uri from the response (C12)", async () => {
      respond(true, { redirect_uri: "javascript:alert(1)" });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() => expect(assignments).toHaveLength(1));
      expect(assignments[0]).toMatch(/^https:\/\/iam\.example\.com\/oidc\/error\?error=server_error&/);
    });

    it("keeps full-page navigation for a marked callback without a code, and clears the marker (Q3)", () => {
      const fetchMock = vi.fn();
      vi.stubGlobal("fetch", fetchMock);
      renderAt("/sso/tenant-1/callback?error=access_denied&state=s1");

      expect(fetchMock).not.toHaveBeenCalled();
      expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
      expect(assignments).toHaveLength(1);
      expect(assignments[0]).toContain("/api/oidc/callback?state=s1&tenant_id=tenant-1&error=access_denied");
    });

    it("lets the device-flow marker win and still clears the app marker (C15)", async () => {
      const deviceReturnUrl = "https://iam.example.com/oidc/device/entry?client_id=dev1";
      sessionStorage.setItem("oidc-device-return-url", deviceReturnUrl);
      const fetchMock = respond(true, { success: true });
      renderAt("/sso/tenant-1/callback?code=c1&state=s1");

      await waitFor(() => expect(assignments).toEqual([deviceReturnUrl]));
      expect(fetchMock).toHaveBeenCalledTimes(1);
      expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
      expect(sessionStorage.getItem("oidc-device-return-url")).toBeNull();
      expect(screen.queryByRole("link", { name: "Return to the app" })).toBeNull();
    });
  });
});
