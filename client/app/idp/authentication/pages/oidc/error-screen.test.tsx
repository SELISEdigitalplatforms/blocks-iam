import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => ({ themeColor: "#123456" }),
}));

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: { template: { theme: {}, branding: { brandName: "Blocks" }, pages: { shared: { footerText: "" } } } } }),
}));

vi.mock("./oidc-auth-shell", () => ({
  OidcAuthShell: ({ children, heading }: { children?: React.ReactNode; heading?: string }) => (
    <div data-testid="shell"><h1>{heading}</h1>{children}</div>
  ),
  OidcFooter: () => null,
  useOidcAuthAnimation: () => null,
}));

import { OIDCErrorScreen } from "./error-screen";

const renderAt = (path: string) =>
  render(
    <MemoryRouter initialEntries={[path]}>
      <OIDCErrorScreen />
    </MemoryRouter>,
  );

describe("OIDCErrorScreen", () => {
  afterEach(() => {
    sessionStorage.clear();
    window.history.replaceState(null, "", "/");
  });

  it("shows the generic blocked message when there is no api error", () => {
    renderAt("/oidc/error");
    expect(screen.getByTestId("shell")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Access Blocked" })).toBeInTheDocument();
    expect(screen.getByText(/couldn.t sign you in/i)).toBeInTheDocument();
  });

  it("shows the api error description and formatted error code", () => {
    renderAt("/oidc/error?error=invalid_request&error_description=Bad%20thing");
    expect(screen.getByRole("heading", { name: "Sign In Failed" })).toBeInTheDocument();
    expect(screen.getByText("Bad thing")).toBeInTheDocument();
    expect(screen.getByText("Invalid Request")).toBeInTheDocument();
  });

  it("explains an expired sign-in in plain words instead of the server text", () => {
    renderAt("/oidc/error?error=invalid_state&error_description=Invalid%20or%20expired%20OIDC%20state");
    expect(screen.getByRole("heading", { name: "Sign-in expired" })).toBeInTheDocument();
    expect(screen.queryByText("Invalid or expired OIDC state")).not.toBeInTheDocument();
  });

  it("tells the user a reopened callback was already completed", () => {
    renderAt("/oidc/error?error=state_already_used");
    expect(screen.getByRole("heading", { name: "Already signed in" })).toBeInTheDocument();
  });

  // The link reads the flow off the real URL (window.location), not the router entry.
  const atUrl = (url: string, name: RegExp = /Back to Sign In/) => {
    window.history.replaceState(null, "", url);
    renderAt(url);
    return screen.getByRole("link", { name });
  };

  it("links back into the sign-in flow when it still holds a client", () => {
    const href = atUrl("/oidc/error?clientId=client-1&state=s1").getAttribute("href");
    expect(href).toMatch(/^\/oidc\/login\?/);
    expect(href).toContain("clientId=client-1");
  });

  it("links back to the application when only its redirect_uri is known", () => {
    expect(
      atUrl("/oidc/error?redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb", /Back to application/),
    ).toHaveAttribute("href", "https://app.example.com");
  });

  it("links back to the application kept before the provider redirect when the URL lost it", () => {
    sessionStorage.setItem("oidc-sso-return-uri", "https://app.example.com/login/callback");
    expect(atUrl("/oidc/error?error=invalid_state", /Back to application/)).toHaveAttribute(
      "href",
      "https://app.example.com",
    );
  });

  it("never links to a bare /oidc/login with no application", () => {
    expect(atUrl("/oidc/error")).toHaveAttribute("href", "/login");
  });

  it("ignores a kept return address that is not an http(s) url", () => {
    sessionStorage.setItem("oidc-sso-return-uri", "javascript:alert(1)");
    expect(atUrl("/oidc/error")).toHaveAttribute("href", "/login");
  });
});
