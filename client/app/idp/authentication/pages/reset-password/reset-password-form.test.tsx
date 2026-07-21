import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountResetPassword: vi.fn(() => ({ isPending: false, mutateAsync: h.mutateAsync })),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: "", reset: vi.fn() })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: undefined, captchaEnabled: false })),
}));
vi.mock(
  "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker",
  () => ({ PasswordStrengthChecker: () => null }),
);
vi.mock("../oidc/oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => null) }));

import { ResetPasswordForm } from "./reset-password-form";

const renderForm = () =>
  render(
    <MemoryRouter>
      <ResetPasswordForm code="reset-code" tenantId="tenant-1" />
    </MemoryRouter>,
  );

const passwordInputs = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('input[type="password"]')) as HTMLInputElement[];

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ResetPasswordForm", () => {
  it("renders the password fields, logout switch, submit and back link", () => {
    const { container } = renderForm();
    expect(screen.getByText("New Password")).toBeInTheDocument();
    expect(screen.getByText("Confirm Password")).toBeInTheDocument();
    expect(screen.getByText("Logout from all devices")).toBeInTheDocument();
    expect(passwordInputs(container)).toHaveLength(2);
    expect(screen.getByRole("button", { name: /set password/i })).toBeDisabled();
    expect(screen.getByRole("link", { name: /back to login/i })).toBeInTheDocument();
  });

  it("flags mismatched passwords", async () => {
    const { container } = renderForm();
    const [password, confirm] = passwordInputs(container);
    fireEvent.change(password, { target: { value: "Passw0rd!" } });
    fireEvent.change(confirm, { target: { value: "Different1!" } });

    expect(await screen.findByText("Passwords must be matched")).toBeInTheDocument();
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });
});
