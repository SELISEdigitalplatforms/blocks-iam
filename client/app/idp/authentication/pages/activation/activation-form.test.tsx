import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
  resetCaptcha: vi.fn(),
  animCtx: null as Record<string, unknown> | null,
}));

vi.mock("react-router", () => ({ useNavigate: () => h.navigateMock }));
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
  fireEvent.change(screen.getByPlaceholderText("First name"), { target: { value: "Grace" } });
  fireEvent.change(screen.getByPlaceholderText("Last name"), { target: { value: "Hopper" } });
  const [password, confirm] = passwordInputs(container);
  fireEvent.change(password, { target: { value: "Passw0rd!" } });
  fireEvent.change(confirm, { target: { value: "Passw0rd!" } });
};

describe("ActivationForm", () => {
  it("renders the name and password fields and a disabled activate button", () => {
    const { container } = render(<ActivationForm code="activation-code" tenantId="tenant-1" />);
    expect(screen.getByText("First Name")).toBeInTheDocument();
    expect(screen.getByText("Last Name")).toBeInTheDocument();
    expect(screen.getByText("Password")).toBeInTheDocument();
    expect(screen.getByText("Confirm Password")).toBeInTheDocument();
    expect(passwordInputs(container)).toHaveLength(2);
    expect(screen.getByRole("button", { name: /activate/i })).toBeDisabled();
  });

  it("requires first and last name before the form is valid", async () => {
    render(<ActivationForm code="activation-code" tenantId="tenant-1" />);
    const firstName = screen.getByPlaceholderText("First name");
    fireEvent.change(firstName, { target: { value: "   " } });

    expect(await screen.findByText("First name is required")).toBeInTheDocument();
    expect(h.mutateAsync).not.toHaveBeenCalled();
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
    expect(container.querySelectorAll('input[autocomplete="new-password"][type="text"]')).toHaveLength(1);
    expect(container.querySelectorAll('input[type="password"]')).toHaveLength(1);
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
      expect(h.navigateMock).toHaveBeenCalledWith(expect.stringContaining("/oidc/activate-success")),
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
    expect(h.navigateMock).not.toHaveBeenCalledWith(expect.stringContaining("/oidc/activate-success"));
  });
});
