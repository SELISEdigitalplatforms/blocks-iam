import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
  resetCaptcha: vi.fn(),
  animCtx: null as Record<string, unknown> | null,
}));

vi.mock("react-router-dom", () => ({ useNavigate: () => h.navigateMock }));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivation: vi.fn(() => ({ isPending: false, mutateAsync: h.mutateAsync })),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: "", reset: h.resetCaptcha })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: undefined, captchaEnabled: false })),
}));
vi.mock("../../components/password-strength-checker/password-strength-checker", () => ({
  PasswordStrengthChecker: () => null,
}));
vi.mock("../oidc/oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => h.animCtx) }));

import { ActivationForm } from "./activation-form";

const passwordInputs = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('input[type="password"]')) as HTMLInputElement[];

beforeEach(() => {
  vi.clearAllMocks();
  h.animCtx = null;
});

const fillValidPasswords = (container: HTMLElement) => {
  const [password, confirm] = passwordInputs(container);
  fireEvent.change(password, { target: { value: "Passw0rd!" } });
  fireEvent.change(confirm, { target: { value: "Passw0rd!" } });
};

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

  it("redirects to login when no activation code is present", () => {
    render(<ActivationForm code="" />);
    expect(h.navigateMock).toHaveBeenCalledWith("/login");
  });

  it("toggles password visibility", () => {
    const { container } = render(<ActivationForm code="activation-code" />);
    const [showNew] = screen.getAllByRole("button", { name: "Show password" });
    fireEvent.click(showNew);
    expect(container.querySelectorAll('input[type="text"]')).toHaveLength(1);
  });

  it("activates the account and navigates to the success page", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const { container } = render(<ActivationForm code="activation-code" tenantId="tenant-1" />);
    fillValidPasswords(container);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await vi.waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ code: "activation-code", password: "Passw0rd!" }),
      ),
    );
    await vi.waitFor(() =>
      expect(h.navigateMock).toHaveBeenCalledWith("/activate-success"),
    );
  });

  it("runs the fail animation and resets the captcha when activation fails", async () => {
    const failAnimation = vi.fn();
    h.animCtx = {
      phase: "idle",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn(),
      failAnimation,
      resetAnimation: vi.fn(),
      setPanelIdleSlot: vi.fn(),
    };
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { password: "Bad code" } });
    const { container } = render(<ActivationForm code="activation-code" />);
    fillValidPasswords(container);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await vi.waitFor(() => expect(failAnimation).toHaveBeenCalledWith("Bad code"));
    expect(h.resetCaptcha).toHaveBeenCalled();
    expect(h.navigateMock).not.toHaveBeenCalledWith("/activate-success");
  });
});
