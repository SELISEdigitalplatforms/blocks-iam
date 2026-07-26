import { render } from "@testing-library/react";
import { Routes, Route, MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ setAuthenticated: vi.fn(), getRuntimeEnv: vi.fn() }));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated }),
}));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: h.getRuntimeEnv }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  getCurrentOIDCParams: () => new URLSearchParams(),
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
  h.getRuntimeEnv.mockReturnValue("https://iam.example.com");
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
});
