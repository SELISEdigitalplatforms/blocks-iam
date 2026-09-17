import { render, waitFor } from "@testing-library/react";
import { Routes, Route, MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ setAuthenticated: vi.fn(), getSelfBaseUrl: vi.fn() }));

vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated }),
}));
vi.mock("@/lib/runtime-env", () => ({ getSelfBaseUrl: h.getSelfBaseUrl }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  getCurrentOIDCParams: () => new URLSearchParams(),
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY: "oidc-device-return-url",
}));

import SSOCallbackPage from "./sso-callback";

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
});
