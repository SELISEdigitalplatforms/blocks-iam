import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";

vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: undefined }),
}));

import { SignupEmailSent } from "./signup-email-sent";

// The login link reads the flow off the real URL (window.location), not the router entry.
const loginLinkAt = (url: string) => {
  window.history.replaceState(null, "", url);
  render(
    <MemoryRouter initialEntries={[url]}>
      <SignupEmailSent email="jane@example.com" />
    </MemoryRouter>,
  );
  return screen.getByRole("link", { name: /Go to login/ });
};

describe("SignupEmailSent login link", () => {
  it("re-enters the OIDC flow when it still holds a client", () => {
    const href = loginLinkAt("/oidc/signup-email-sent?clientId=client-1&state=s1").getAttribute("href");
    expect(href).toMatch(/^\/oidc\/login\?/);
    expect(href).toContain("clientId=client-1");
  });

  it("goes to IAM's own login, not a bare /oidc/login, when the flow is lost", () => {
    expect(loginLinkAt("/oidc/signup-email-sent")).toHaveAttribute("href", "/login");
  });
});
