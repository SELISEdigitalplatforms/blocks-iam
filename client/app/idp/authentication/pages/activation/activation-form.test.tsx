import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
}));

vi.mock("react-router-dom", () => ({ useNavigate: () => h.navigateMock }));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivation: vi.fn(() => ({ isPending: false, mutateAsync: h.mutateAsync })),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: "", reset: vi.fn() })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: undefined, captchaEnabled: false })),
}));
vi.mock("../../components/password-strength-checker/password-strength-checker", () => ({
  PasswordStrengthChecker: () => null,
}));
vi.mock("../oidc/oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => null) }));

import { ActivationForm } from "./activation-form";

const passwordInputs = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('input[type="password"]')) as HTMLInputElement[];

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ActivationForm", () => {
  it("renders both password fields and a disabled activate button", () => {
    const { container } = render(<ActivationForm code="activation-code" tenantId="tenant-1" />);
    expect(screen.getByText("Password")).toBeInTheDocument();
    expect(screen.getByText("Confirm Password")).toBeInTheDocument();
    expect(passwordInputs(container)).toHaveLength(2);
    expect(screen.getByRole("button", { name: /activate/i })).toBeDisabled();
  });

  it("validates that the password may not contain whitespace", async () => {
    const { container } = render(<ActivationForm code="activation-code" />);
    const [password] = passwordInputs(container);
    fireEvent.change(password, { target: { value: "has space" } });

    expect(
      await screen.findByText("Password must not contain spaces"),
    ).toBeInTheDocument();
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });
});
