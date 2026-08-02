import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  verifyOidc: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  getRuntimeEnv: vi.fn(),
}));

vi.mock("react-router", async (orig) => {
  const actual = (await orig()) as Record<string, unknown>;
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@blocks-idp/authentication/pages/oidc/permission-wrapper", () => ({
  OIDCPermissionWrapper: () => <div data-testid="permission-wrapper" />,
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-signin", () => ({
  OIDCSignin: () => <div data-testid="oidc-signin" />,
}));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { verifyOidc: h.verifyOidc },
}));
vi.mock("@seliseblocks/genesis-os", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated, setTokens: h.setTokens }),
}));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: h.getRuntimeEnv }));

import OidcIndexPage from "./index";

const renderAt = (search: string) =>
  render(
    <MemoryRouter initialEntries={[`/oidc${search}`]}>
      <OidcIndexPage />
    </MemoryRouter>,
  );

const originalLocation = window.location;
beforeEach(() => {
  vi.clearAllMocks();
  h.getRuntimeEnv.mockReturnValue("https://localhost:4000");
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { ...originalLocation, href: "http://localhost/", origin: "http://localhost" },
  });
});
afterEach(() => {
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
});

describe("OidcIndexPage", () => {
  it("renders the sign-in screen with no query params", () => {
    renderAt("");
    expect(screen.getByTestId("oidc-signin")).toBeInTheDocument();
  });

  it("renders the permission wrapper when a userName is present", () => {
    renderAt("?userName=jane");
    expect(screen.getByTestId("permission-wrapper")).toBeInTheDocument();
  });

  it("exchanges the code, stores localhost tokens and redirects to console", async () => {
    h.verifyOidc.mockResolvedValue({
      access_token: "at",
      refresh_token: "rt",
    });
    renderAt("?code=c1&state=s1");

    await vi.waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    expect(h.verifyOidc).toHaveBeenCalledWith({ code: "c1", state: "s1" });
    expect(h.setTokens).toHaveBeenCalledWith("at", "rt");
    expect(window.location.href).toBe("http://localhost/console");
  });

  it("navigates to the error route when the exchange fails", async () => {
    h.verifyOidc.mockRejectedValue(new Error("bad"));
    renderAt("?code=c1&state=s1");
    await vi.waitFor(() => expect(h.navigate).toHaveBeenCalledWith("/oidc/error"));
  });
});
