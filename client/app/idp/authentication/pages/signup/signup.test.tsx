import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  signUpSetting: undefined as Record<string, boolean> | undefined,
  isSignUpSettingLoading: false,
  loginOption: undefined as { ssoInfo?: unknown[] } | undefined,
  isLoginOptionLoading: false,
}));

vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: () => ({ data: h.loginOption, isLoading: h.isLoginOptionLoading }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => ({ data: h.signUpSetting, isLoading: h.isSignUpSettingLoading }),
}));
vi.mock("./signup-form", () => ({ SignupForm: () => <div data-testid="signup-form" /> }));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  OidcAuthShell: ({ children, footerNote }: { children: React.ReactNode; footerNote: React.ReactNode }) => (
    <div>
      {footerNote}
      {children}
    </div>
  ),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-panel-config", () => ({ SIGNUP_PANEL: {} }));

import { Signup } from "./signup";

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
});
