import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  loginOption: undefined as { allowedGrantTypes: string[] } | undefined,
  isLoginOptionLoading: false,
  signUpSetting: undefined as { isSignUpEnable: boolean } | undefined,
  isSignUpSettingLoading: false,
  showError: vi.fn(),
}));

vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: () => ({ data: h.loginOption, isLoading: h.isLoginOptionLoading }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => ({ data: h.signUpSetting, isLoading: h.isSignUpSettingLoading }),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => h.showError(a) }));
vi.mock("./signin-form", () => ({ SigninForm: () => <div data-testid="signin-form" /> }));
vi.mock("./sso-signin", () => ({ SsoSignin: () => <div data-testid="sso-signin" /> }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: () => ({ tenantId: "" }),
  buildOIDCNavigationUrl: (p: string) => p,
}));

import { Signin } from "./signin";

const renderSignin = (props = {}) =>
  render(
    <MemoryRouter>
      <Signin {...props} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.loginOption = { allowedGrantTypes: ["password", "social"] };
  h.isLoginOptionLoading = false;
  h.signUpSetting = { isSignUpEnable: true };
  h.isSignUpSettingLoading = false;
});

describe("Signin", () => {
  it("renders the loading skeleton while options are loading", () => {
    h.isLoginOptionLoading = true;
    const { container } = renderSignin();
    expect(container.querySelector("[class*='animate-pulse']")).not.toBeNull();
    expect(screen.queryByTestId("signin-form")).not.toBeInTheDocument();
  });

  it("renders nothing when there are no allowed grant types", () => {
    h.loginOption = { allowedGrantTypes: [] };
    const { container } = renderSignin();
    expect(container.querySelector('[class*="animate-pulse"]')).toBeNull();
    expect(screen.queryByTestId("signin-form")).not.toBeInTheDocument();
  });

  it("renders the password form, SSO block and OR divider", () => {
    renderSignin();
    expect(screen.getByTestId("signin-form")).toBeInTheDocument();
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
    expect(screen.getByText("OR")).toBeInTheDocument();
  });

  it("shows the sign-up link when sign-up is enabled", () => {
    renderSignin();
    expect(screen.getByRole("link", { name: "Sign up" })).toHaveAttribute(
      "href",
      "/oidc/signup",
    );
  });

  it("hides the sign-up link in oidc mode", () => {
    renderSignin({ mode: "oidc" });
    expect(screen.queryByRole("link", { name: "Sign up" })).not.toBeInTheDocument();
  });

  it("shows an error toast when an ssoError is passed", () => {
    renderSignin({ ssoError: "sso failed" });
    expect(h.showError).toHaveBeenCalledWith({ errors: "sso failed" });
  });
});
