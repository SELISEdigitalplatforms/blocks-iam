import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
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

vi.mock("@blocks-idp/authentication/pages/auth-logs", () => ({ AuthLogs: stub("auth-logs") }));
vi.mock("@blocks-idp/authentication/pages/authentication-config", () => ({
  AuthenticationConfig: stub("authentication-config"),
}));
vi.mock("@blocks-idp/captcha/pages/configure-captcha", () => ({
  ConfigureCaptcha: stub("configure-captcha"),
}));
vi.mock("@blocks-idp/iam/modules/permission-management", () => ({
  AddPermission: stub("add-permission"),
}));
vi.mock("@blocks-idp/iam/modules/user-management", () => ({
  Configure: stub("configure"),
  IamLogs: stub("iam-logs"),
  User: stub("user"),
}));
vi.mock("@blocks-idp/iam/pages/organization-detail/organization-detail", () => ({
  OrganizationDetail: stub("organization-detail"),
}));
vi.mock("@blocks-idp/iam/modules/permission-management/permission-details", () => ({
  PermissionDetails: stub("permission-details"),
}));
vi.mock("@blocks-idp/iam/modules/role-management", () => ({
  RoleDetails: stub("role-details"),
}));
vi.mock("@blocks-idp/iam/pages/iam-management", () => ({ IamManagement: stub("iam-management") }));
vi.mock("@blocks-identifier/pages/services/managed-services", () => ({
  ManagedServices: stub("managed-services"),
}));
vi.mock("@blocks-idp/mfa/pages/configure-mfa/configure-mfa", () => ({
  ConfigureMFA: stub("configure-mfa"),
}));
vi.mock("@blocks-idp/iam/modules/user-management/profile", () => ({ Profile: stub("profile") }));
vi.mock("@blocks-idp/authentication/pages/sso-configuration", () => ({
  SSOConfiguration: stub("sso-configuration"),
}));

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
vi.mock("@blocks-idp/authentication/pages/login", () => ({ Signin: stub("signin") }));
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
vi.mock("@blocks-idp/authentication/pages/oidc/email-sent-confirmation/email-sent-confirmation", () => ({
  OidcEmailConfirmation: stub("oidc-email-confirmation"),
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
  const Route = mod.default as React.ComponentType<Record<string, unknown>>;
  render(
    <MemoryRouter initialEntries={initialEntries}>
      <Route section="users" />
    </MemoryRouter>,
  );
  expect(screen.getByTestId(testId)).toBeInTheDocument();
};

describe("dashboard route wrappers", () => {
  it.each([
    ["./dashboard/auth-logs", "auth-logs"],
    ["./dashboard/authentication-config", "authentication-config"],
    ["./dashboard/captcha-config", "configure-captcha"],
    ["./dashboard/iam-add-permission", "add-permission"],
    ["./dashboard/iam-configure", "configure"],
    ["./dashboard/iam-logs", "iam-logs"],
    ["./dashboard/iam", "iam-management"],
    ["./dashboard/managed-services", "managed-services"],
    ["./dashboard/mfa-config", "configure-mfa"],
    ["./dashboard/profile", "profile"],
    ["./dashboard/sso-configuration", "sso-configuration"],
  ])("renders %s", async (path, testId) => {
    await renderRoute(path, testId);
  });

  it.each([
    ["./dashboard/iam-org-detail", "organization-detail"],
    ["./dashboard/iam-permission-detail", "permission-details"],
    ["./dashboard/iam-role-detail", "role-details"],
    ["./dashboard/iam-user-detail", "user"],
  ])("renders param route %s", async (path, testId) => {
    await renderRoute(path, testId, ["/x/abc"]);
  });

  it("renders the static rate limiter page", async () => {
    const mod = await import("./dashboard/rate-limiter");
    const Route = mod.default;
    render(<Route />);
    expect(screen.getByText("Rate Limiter")).toBeInTheDocument();
  });

  it.each([
    ["./dashboard/captcha-logs"],
    ["./dashboard/mfa-logs"],
  ])("renders redirect route %s without throwing", async (path) => {
    const mod = await import(path);
    const Route = mod.default;
    expect(() =>
      render(
        <MemoryRouter>
          <Route />
        </MemoryRouter>,
      ),
    ).not.toThrow();
  });
});

describe("auth / oidc / device route wrappers", () => {
  it.each([
    ["./auth/activate-success", "activation-success"],
    ["./auth/activate", "activation"],
    ["./auth/forgot-email-sent", "forgot-email-sent"],
    ["./auth/forgot-password", "oidc-forgot-password"],
    ["./auth/login", "signin"],
    ["./auth/mfa-check", "mfa-check"],
    ["./auth/reset-password-success", "reset-password-success"],
    ["./auth/resetpassword", "reset-password"],
    ["./auth/signup-email-sent", "signup-email-sent"],
    ["./auth/signup", "signup"],
    ["./oidc/email-sent-confirmation", "oidc-email-confirmation"],
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
