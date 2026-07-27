import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

vi.mock("@/layouts/oidc-layout", () => ({
  useOIDCContext: () => ({ themeColor: "#123456" }),
}));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (path: string) => path,
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

  it("links back to the sign-in page", () => {
    renderAt("/oidc/error");
    expect(screen.getByRole("link", { name: /Back to Sign In/ })).toHaveAttribute(
      "href",
      "/oidc/login",
    );
  });
});
