import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router";
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
  uiConfigLoading: false,
  loginOptionsLoading: false,
  signUpSettingLoading: false,
  useGetLoginOptions: vi.fn(),
  useGetSignUpSetting: vi.fn(),
  uiConfigLoaded: false,
  shellProps: null as Record<string, unknown> | null,
  formProps: null as Record<string, unknown> | null,
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
  OidcLoginForm: (props: { clientId: string; redirectUri: string }) => {
    h.formProps = props;
    return (
      <div data-testid="login-form">
        form client={props.clientId} redirect={props.redirectUri}
      </div>
    );
  },
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({
    data: h.oidcUiConfig,
    isLoading: h.uiConfigLoading,
    isSuccess: h.uiConfigLoaded,
  }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: (...args: unknown[]) => h.useGetLoginOptions(...args),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: (...args: unknown[]) => h.useGetSignUpSetting(...args),
}));

import { OIDCSignin } from "./oidc-signin";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

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
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
  h.uiConfigLoading = false;
  h.uiConfigLoaded = false;
  h.formProps = null;
  localStorage.clear();
  h.loginOptionsLoading = false;
  h.signUpSettingLoading = false;
  h.useGetLoginOptions.mockImplementation(() => ({ isLoading: h.loginOptionsLoading }));
  h.useGetSignUpSetting.mockImplementation(() => ({ isLoading: h.signUpSettingLoading }));
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
        layout: { showAnimationPanel: false },
        branding: {
          logoUrlLight: "https://example.test/logo-light.svg",
          logoUrlDark: "https://example.test/logo-dark.svg",
          brandName: "Acme",
        },
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          login: { ...OIDC_UI_TEMPLATE_FIXTURE.pages.login, heading: "Welcome to Acme" },
        },
      },
    };
    renderAt("/oidc/login?client_id=client-1&redirect_uri=https://app/cb");
    expect(h.shellProps).toEqual(expect.objectContaining({
      showAnimationPanel: false,
      heading: "Welcome to Acme",
      logoUrlLight: "https://example.test/logo-light.svg",
      logoUrlDark: "https://example.test/logo-dark.svg",
      brandName: "Acme",
      theme: OIDC_UI_TEMPLATE_FIXTURE.theme,
    }));
  });

  describe("loading the page's configuration", () => {
    const LOGIN_URL = "/oidc/login?client_id=client-1&redirect_uri=https://app/cb&tenant_id=tenant-1";

    beforeEach(() => {
      setPath("/oidc/login");
      h.context = { tenantId: "tenant-1" };
    });

    const expectSkeletonOnly = () => {
      expect(screen.getByTestId("oidc-login-page-skeleton")).toBeInTheDocument();
      expect(screen.getByRole("status")).toHaveTextContent("Loading sign-in options…");
      expect(screen.queryByTestId("auth-shell")).not.toBeInTheDocument();
      expect(screen.queryByTestId("login-form")).not.toBeInTheDocument();
    };

    it("shows the page skeleton while the tenant's template loads", () => {
      h.uiConfigLoading = true;
      renderAt(LOGIN_URL);
      expectSkeletonOnly();
    });

    it("shows the page skeleton while the application's login options load", () => {
      h.loginOptionsLoading = true;
      renderAt(LOGIN_URL);
      expectSkeletonOnly();
    });

    it("shows the page skeleton while the sign-up setting loads", () => {
      h.signUpSettingLoading = true;
      renderAt(LOGIN_URL);
      expectSkeletonOnly();
    });

    it("renders the configured page in one step once all three are in", () => {
      renderAt(LOGIN_URL);
      expect(screen.queryByTestId("oidc-login-page-skeleton")).not.toBeInTheDocument();
      expect(screen.getByTestId("auth-shell")).toBeInTheDocument();
      expect(screen.getByTestId("login-form")).toBeInTheDocument();
    });

    it("asks for the login options and sign-up setting alongside the template, keyed like the form", () => {
      h.uiConfigLoading = true;
      renderAt(LOGIN_URL);
      // Same (tenant, client) as the form's own calls, so the form reads them from the cache.
      expect(h.useGetLoginOptions).toHaveBeenCalledWith("tenant-1", true, "client-1");
      expect(h.useGetSignUpSetting).toHaveBeenCalledWith("tenant-1", { enabled: true });
    });

    it("shapes the skeleton like this tenant's last-seen layout", () => {
      localStorage.setItem("oidc-login-side-panel:tenant-1", "false");
      h.uiConfigLoading = true;
      const { container } = renderAt(LOGIN_URL);
      expect(container.querySelector("main > div")).toHaveClass("max-w-lg");
    });

    it("uses the default layout for the skeleton on a first visit", () => {
      h.uiConfigLoading = true;
      const { container } = renderAt(LOGIN_URL);
      expect(container.querySelector("main > div")).toHaveClass("md:flex-row");
    });

    it("remembers the tenant's side-panel setting once the template is in", () => {
      h.uiConfigLoaded = true;
      h.oidcUiConfig = {
        captcha: null,
        template: { ...OIDC_UI_TEMPLATE_FIXTURE, layout: { showAnimationPanel: false } },
      };
      renderAt(LOGIN_URL);
      expect(localStorage.getItem("oidc-login-side-panel:tenant-1")).toBe("false");
    });

    it("lets the form start a single provider by itself", () => {
      renderAt(LOGIN_URL);
      expect(h.formProps).toEqual(expect.objectContaining({ autoStartSingleProvider: true }));
    });

    it("does not let it on a return trip that carries a provider code", async () => {
      h.signinBySSO.mockRejectedValue(new Error("expired"));
      renderAt(`${LOGIN_URL}&code=abc&state=xyz`);
      await waitFor(() => expect(h.formProps).not.toBeNull());
      expect(h.formProps).toEqual(expect.objectContaining({ autoStartSingleProvider: false }));
    });

    it("does not ask for them outside the sign-in form flow", () => {
      setPath("/oidc/callback");
      renderAt("/oidc/callback?error=denied");
      expect(h.useGetLoginOptions).toHaveBeenCalledWith("tenant-1", false, "");
      expect(h.useGetSignUpSetting).toHaveBeenCalledWith("tenant-1", { enabled: false });
      expect(screen.queryByTestId("oidc-login-page-skeleton")).not.toBeInTheDocument();
    });
  });

  it("uses url client_id / redirect_uri when the context lacks them", () => {
    setPath("/oidc/login");
    h.context = {};
    renderAt("/oidc/login?client_id=url-client&redirect_uri=https://app/url-cb");

    const form = screen.getByTestId("login-form");
    expect(form).toHaveTextContent("client=url-client");
    expect(form).toHaveTextContent("redirect=https://app/url-cb");
  });

  describe("/oidc/login with no application", () => {
    const renderWithLoginRoute = (entry: string) =>
      render(
        <MemoryRouter initialEntries={[entry]}>
          <Routes>
            <Route path="/oidc/login" element={<OIDCSignin />} />
            <Route path="/login" element={<div data-testid="iam-login" />} />
          </Routes>
        </MemoryRouter>,
      );

    beforeEach(() => setPath("/oidc/login"));

    it("redirects to IAM's own /login instead of the dead-end card", () => {
      renderWithLoginRoute("/oidc/login?brandColor=%23124091");
      expect(screen.getByTestId("iam-login")).toBeInTheDocument();
      expect(screen.queryByTestId("signin")).not.toBeInTheDocument();
    });

    it("stays when the link names a tenant", () => {
      renderWithLoginRoute("/oidc/login?tenant_id=tenant-1");
      expect(screen.queryByTestId("iam-login")).not.toBeInTheDocument();
      expect(screen.getByTestId("signin")).toBeInTheDocument();
    });

    it("stays when the flow context holds a client", () => {
      h.context = { clientId: "client-1" };
      renderWithLoginRoute("/oidc/login");
      expect(screen.queryByTestId("iam-login")).not.toBeInTheDocument();
    });

    it("stays for a device-flow return trip", () => {
      renderWithLoginRoute("/oidc/login?returnUrl=https%3A%2F%2Fiam.example.com%2Fdevice%2Ft");
      expect(screen.queryByTestId("iam-login")).not.toBeInTheDocument();
    });

    it("stays while a social sign-in code is being redeemed", () => {
      h.signinBySSO.mockReturnValue(new Promise(() => {}));
      renderWithLoginRoute("/oidc/login?code=abc&state=xyz");
      expect(screen.queryByTestId("iam-login")).not.toBeInTheDocument();
    });
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
