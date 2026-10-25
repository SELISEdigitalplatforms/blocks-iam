import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => ({ themeColor: "#123456" }),
}));

import { OIDCErrorScreen } from "./error-screen";

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <OIDCErrorScreen />
    </MemoryRouter>,
  );

describe("OIDCErrorScreen", () => {
  it("shows the generic blocked message when there is no api error", () => {
    renderAt("/oidc/error");
    expect(screen.getByText("Access Blocked")).toBeInTheDocument();
    expect(screen.getByText(/couldn.t sign you in/i)).toBeInTheDocument();
  });

  it("shows the api error description and formatted error code", () => {
    renderAt("/oidc/error?error=invalid_request&error_description=Bad%20thing");
    expect(screen.getByText("Sign In Failed")).toBeInTheDocument();
    expect(screen.getByText("Bad thing")).toBeInTheDocument();
    expect(screen.getByText("Invalid Request")).toBeInTheDocument();
  });

  // The link reads the flow off the real URL (window.location), not the router entry.
  const atUrl = (url: string) => {
    window.history.replaceState(null, "", url);
    renderAt(url);
    return screen.getByRole("link", { name: /Back to Sign In/ });
  };

  it("links back into the sign-in flow when it still holds a client", () => {
    const href = atUrl("/oidc/error?clientId=client-1&state=s1").getAttribute("href");
    expect(href).toMatch(/^\/oidc\/login\?/);
    expect(href).toContain("clientId=client-1");
  });

  it("links back to the application when only its redirect_uri is known", () => {
    expect(atUrl("/oidc/error?redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb")).toHaveAttribute(
      "href",
      "https://app.example.com",
    );
  });

  it("never links to a bare /oidc/login with no application", () => {
    expect(atUrl("/oidc/error")).toHaveAttribute("href", "/login");
  });
});
