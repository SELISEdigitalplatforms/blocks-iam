import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { describe, expect, it, vi } from "vitest";

// Every route wrapper simply renders a page component (sometimes inside a
// layout div and/or forwarding route params). We stub each page module so the
// test asserts the wrapper wires the child in without pulling the heavy page
// tree into the graph.
const stub = (id: string) => {
  const Stub = ({ children }: { children?: React.ReactNode }) => (
    <div data-testid={id}>{children}</div>
  );
  Stub.displayName = `Stub(${id})`;
  return Stub;
};

vi.mock("@blocks-idp/iam/modules/user-management/profile", () => ({ Profile: stub("profile") }));

// Auth + oidc + device wrappers.
vi.mock("@blocks-idp/authentication/pages/activation-success", () => ({
  ActivationSuccess: stub("activation-success"),
}));
vi.mock("@blocks-idp/authentication/pages/activation", () => ({ Activation: stub("activation") }));
vi.mock("@blocks-idp/authentication/pages/forgot-email-sent", () => ({
  ForgotEmailSent: stub("forgot-email-sent"),
}));
vi.mock("@/idp/authentication/pages/oidc-forgot-password", () => ({
  OIDCForgotPassword: stub("oidc-forgot-password"),
}));
vi.mock("@blocks-idp/authentication/pages/mfa-check", () => ({ MfaCheck: stub("mfa-check") }));
vi.mock("@blocks-idp/authentication/pages/reset-password-success", () => ({
  ResetPasswordSuccess: stub("reset-password-success"),
}));
vi.mock("@blocks-idp/authentication/pages/reset-password", () => ({
  ResetPassword: stub("reset-password"),
}));
vi.mock("@blocks-idp/authentication/pages/signup-email-sent", () => ({
  SignupEmailSent: stub("signup-email-sent"),
}));
vi.mock("@blocks-idp/authentication/pages/signup", () => ({ Signup: stub("signup") }));
vi.mock("@blocks-idp/authentication/pages/sso-activate", () => ({
  SsoActivate: stub("sso-activate"),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/error-screen", () => ({
  OIDCErrorScreen: stub("oidc-error-screen"),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-signin", () => ({
  OIDCSignin: stub("oidc-signin"),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/permission-wrapper", () => ({
  OIDCPermissionWrapper: stub("oidc-permission-wrapper"),
}));
vi.mock("@blocks-idp/authentication/pages/device/entry", () => ({
  DeviceEntryPage: stub("device-entry"),
}));
vi.mock("@blocks-idp/authentication/pages/device/success", () => ({
  DeviceSuccessPage: stub("device-success"),
}));

const renderRoute = async (path: string, testId: string, initialEntries = ["/"]) => {
  const mod = await import(path);
  const Route = mod.default as React.ComponentType;
  render(
    <MemoryRouter initialEntries={initialEntries}>
      <Route />
    </MemoryRouter>,
  );
  expect(screen.getByTestId(testId)).toBeInTheDocument();
};

describe("dashboard route wrappers", () => {
  it("renders ./dashboard/profile", async () => {
    await renderRoute("./dashboard/profile", "profile");
  });
});

describe("auth / oidc / device route wrappers", () => {
  it.each([
    ["./auth/activate-success", "activation-success"],
    ["./auth/activate", "activation"],
    ["./auth/forgot-email-sent", "forgot-email-sent"],
    ["./auth/forgot-password", "oidc-forgot-password"],
    ["./auth/mfa-check", "mfa-check"],
    ["./auth/reset-password-success", "reset-password-success"],
    ["./auth/resetpassword", "reset-password"],
    ["./auth/signup-email-sent", "signup-email-sent"],
    ["./auth/signup", "signup"],
    ["./oidc/error", "oidc-error-screen"],
    ["./oidc/login", "oidc-signin"],
    ["./oidc/permission", "oidc-permission-wrapper"],
    ["./device/index", "device-entry"],
    ["./device/success/index", "device-success"],
  ])("renders %s", async (path, testId) => {
    await renderRoute(path, testId, ["/x?token=t&code=c"]);
  });

  it("renders SsoActivate when code and username are present", async () => {
    const mod = await import("./auth/sso-activate");
    const Route = mod.default;
    render(
      <MemoryRouter initialEntries={["/x?code=c&username=u"]}>
        <Route />
      </MemoryRouter>,
    );
    expect(screen.getByTestId("sso-activate")).toBeInTheDocument();
  });

  it("redirects from sso-activate when the params are missing", async () => {
    const mod = await import("./auth/sso-activate");
    const Route = mod.default;
    render(
      <MemoryRouter initialEntries={["/x"]}>
        <Route />
      </MemoryRouter>,
    );
    expect(screen.queryByTestId("sso-activate")).not.toBeInTheDocument();
  });
});
