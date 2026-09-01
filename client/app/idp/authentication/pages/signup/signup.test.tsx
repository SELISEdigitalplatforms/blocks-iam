import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  signUpSetting: undefined as Record<string, boolean> | undefined,
  isSignUpSettingLoading: false,
  loginOption: undefined as { ssoInfo?: unknown[] } | undefined,
  isLoginOptionLoading: false,
  orgConfig: undefined as Record<string, boolean> | undefined,
  isOrgConfigLoading: false,
  oidcUiConfig: undefined as unknown,
  shellProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: () => ({ data: h.loginOption, isLoading: h.isLoginOptionLoading }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => ({ data: h.signUpSetting, isLoading: h.isSignUpSettingLoading }),
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetSignupOrganizationConfig: () => ({ data: h.orgConfig, isLoading: h.isOrgConfigLoading }),
}));
vi.mock("./signup-form", () => ({ SignupForm: () => <div data-testid="signup-form" /> }));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: (props: Record<string, unknown>) => {
    h.shellProps = props;
    return <div>{props.footerNote as React.ReactNode}{props.children as React.ReactNode}</div>;
  },
  OidcFooter: ({ footerText }: { footerText: string }) => <span>{footerText}</span>,
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-panel-config", () => ({ SIGNUP_PANEL: {} }));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));

import { Signup } from "./signup";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const renderSignup = (props = {}) =>
  render(
    <MemoryRouter>
      <Signup {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.signUpSetting = undefined;
  h.isSignUpSettingLoading = false;
  h.loginOption = undefined;
  h.isLoginOptionLoading = false;
  h.orgConfig = undefined;
  h.isOrgConfigLoading = false;
  h.oidcUiConfig = undefined;
  h.shellProps = null;
});

describe("Signup", () => {
  it("renders a loading skeleton while sign-up settings load", () => {
    h.isSignUpSettingLoading = true;
    const { container } = renderSignup();
    expect(container.querySelector("[class*='animate-pulse']")).not.toBeNull();
    expect(screen.queryByTestId("signup-form")).not.toBeInTheDocument();
  });

  it("renders the sign-up form when email sign-up is enabled", () => {
    h.signUpSetting = { isSignUpEnable: true, isEmailPasswordSignUpEnabled: true };
    renderSignup();
    expect(screen.getByTestId("signup-form")).toBeInTheDocument();
  });

  it("does not render the form when sign-up is disabled", () => {
    h.signUpSetting = { isSignUpEnable: false, isEmailPasswordSignUpEnabled: true };
    renderSignup();
    expect(screen.queryByTestId("signup-form")).not.toBeInTheDocument();
  });

  it("renders the sign-in footer link", () => {
    h.signUpSetting = { isSignUpEnable: true, isEmailPasswordSignUpEnabled: true };
    renderSignup();
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
  });

  it("passes tenant-defined signup and success copy to the shell", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          signup: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.signup,
            heading: "Join Acme",
            successTitle: "Welcome aboard",
            successSubtitle: "Check your Acme inbox",
            loginPrompt: "Have an Acme account?",
            loginLink: "Return to sign in",
          },
        },
      },
    };
    renderSignup();
    expect(h.shellProps).toEqual(expect.objectContaining({
      heading: "Join Acme",
      successTitle: "Welcome aboard",
      successSubtitle: "Check your Acme inbox",
    }));
    expect(screen.getByText("Have an Acme account?")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Return to sign in" })).toBeInTheDocument();
  });
});
