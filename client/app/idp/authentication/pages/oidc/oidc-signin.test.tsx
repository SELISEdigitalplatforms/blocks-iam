import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, afterEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  signinBySSO: vi.fn(),
  showErrorToast: vi.fn(),
  context: {} as Record<string, unknown>,
  getCurrentOIDCParams: vi.fn(),
  buildOIDCNavigationUrl: vi.fn((p: string) => `built:${p}`),
  oidcUiConfig: undefined as unknown,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({
    setAuthenticated: h.setAuthenticated,
    setTokens: h.setTokens,
  })),
}));
vi.mock("@blocks-idp/authentication/services/oauth.service", () => ({
  oauthService: { signinBySSO: h.signinBySSO },
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: h.showErrorToast }));
vi.mock("@/layouts/oidc-layout", () => ({ useOIDCContext: () => h.context }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: h.buildOIDCNavigationUrl,
  getCurrentOIDCParams: h.getCurrentOIDCParams,
}));
vi.mock("@blocks-idp/authentication/pages/login", () => ({
  Signin: ({ mode, ssoError }: { mode: string; ssoError?: string }) => (
    <div data-testid="signin">
      signin mode={mode} error={ssoError ?? "none"}
    </div>
  ),
}));
vi.mock("./oidc-auth-shell", () => ({
  OidcAuthShell: (props: Record<string, unknown>) => {
    h.shellProps = props;
    return <div data-testid="auth-shell">{props.children as React.ReactNode}</div>;
  },
  OidcFooter: ({ footerText }: { footerText: string }) => <span>{footerText}</span>,
}));
vi.mock("./oidc-login-form", () => ({
  OidcLoginForm: (props: { clientId: string; redirectUri: string }) => (
    <div data-testid="login-form">
      form client={props.clientId} redirect={props.redirectUri}
    </div>
  ),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { OIDCSignin } from "./oidc-signin";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const setPath = (pathname: string) => {
  Object.defineProperty(window, "location", {
    configurable: true,
    value: { pathname, origin: "https://iam.example.com", href: "" },
  });
};

const renderAt = (entry: string) =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <OIDCSignin />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.context = {};
  h.getCurrentOIDCParams.mockReturnValue(new URLSearchParams());
  h.oidcUiConfig = undefined;
  h.shellProps = null;
  setPath("/oidc/callback");
});

afterEach(() => {
  vi.restoreAllMocks();
});

describe("OIDCSignin", () => {
  it("falls back to the standard Signin when not in the password flow", () => {
    renderAt("/oidc/callback?error=denied");
    const signin = screen.getByTestId("signin");
    expect(signin).toHaveTextContent("mode=oidc");
    expect(signin).toHaveTextContent("error=denied");
  });

  it("renders the sci-fi shell + login form for the OIDC password flow", () => {
    setPath("/oidc/login");
    h.context = { clientId: "client-1", redirectUri: "https://app/cb" };
    renderAt("/oidc/login?client_id=client-1&redirect_uri=https://app/cb");

    expect(screen.getByTestId("auth-shell")).toBeInTheDocument();
    const form = screen.getByTestId("login-form");
    expect(form).toHaveTextContent("client=client-1");
  });

  it("passes tenant-defined login heading, theme and branding to the shell", () => {
    setPath("/oidc/login");
    h.context = { clientId: "client-1", redirectUri: "https://app/cb" };
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        branding: { logoUrl: "https://example.test/logo.svg", brandName: "Acme" },
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          login: { ...OIDC_UI_TEMPLATE_FIXTURE.pages.login, heading: "Welcome to Acme" },
        },
      },
    };
    renderAt("/oidc/login?client_id=client-1&redirect_uri=https://app/cb");
    expect(h.shellProps).toEqual(expect.objectContaining({
      heading: "Welcome to Acme",
      logoUrl: "https://example.test/logo.svg",
      brandName: "Acme",
      theme: OIDC_UI_TEMPLATE_FIXTURE.theme,
    }));
  });

  it("uses url client_id / redirect_uri when the context lacks them", () => {
    setPath("/oidc/login");
    h.context = {};
    renderAt("/oidc/login?client_id=url-client&redirect_uri=https://app/url-cb");

    const form = screen.getByTestId("login-form");
    expect(form).toHaveTextContent("client=url-client");
    expect(form).toHaveTextContent("redirect=https://app/url-cb");
  });

  it("completes social sign-in and navigates to the permission screen", async () => {
    h.context = { clientId: "client-1", userName: "jane" };
    h.signinBySSO.mockResolvedValue({});
    renderAt("/oidc/callback?code=abc&state=xyz");

    // Loader while activating.
    await waitFor(() => expect(h.signinBySSO).toHaveBeenCalledWith({
      code: "abc",
      state: "xyz",
      clientId: "client-1",
    }));
    expect(h.setAuthenticated).toHaveBeenCalled();
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith(
        expect.stringContaining("/oidc/permission?"),
      ),
    );
  });

  it("redirects to the MFA check when the SSO response requires MFA", async () => {
    h.context = { clientId: "client-1" };
    h.signinBySSO.mockResolvedValue({
      error: "mfa_enabled",
      mfa_required: true,
      mfa_id: "m1",
      mfa_type: 2,
    });
    renderAt("/oidc/callback?code=abc&state=xyz");

    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("built:/mfa-check?mfa_id=m1&mfa_type=2"),
    );
    // The session is only marked authenticated once the second factor clears.
    expect(h.setAuthenticated).not.toHaveBeenCalled();
  });

  it("follows the SSO redirect url when the response supplies one", async () => {
    h.context = { clientId: "client-1" };
    h.signinBySSO.mockResolvedValue({ redirect_url: "https://done.example.com" });
    renderAt("/oidc/callback?code=abc&state=xyz");

    await waitFor(() =>
      expect(window.location.href).toBe("https://done.example.com"),
    );
  });

  it("shows an error toast when social sign-in fails", async () => {
    h.context = { clientId: "client-1" };
    h.signinBySSO.mockRejectedValue(new Error("boom"));
    renderAt("/oidc/callback?code=abc&state=xyz");

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({
        errors: "Social sign in failed. Please try again.",
      }),
    );
  });
});
