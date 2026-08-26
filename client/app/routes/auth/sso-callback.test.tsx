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
  });
});
