import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
  resetCaptcha: vi.fn(),
  animCtx: null as Record<string, unknown> | null,
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
  useCaptcha: vi.fn(() => ({ captcha: {}, code: "", reset: h.resetCaptcha })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: undefined, captchaEnabled: false })),
}));
vi.mock(
  "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker",
  () => ({ PasswordStrengthChecker: () => null }),
);
vi.mock("../oidc/oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => h.animCtx) }));

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
  h.animCtx = null;
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

  it("toggles visibility of both password fields", () => {
    const { container } = renderForm();
    const [password, confirm] = passwordInputs(container);
    expect(password.type).toBe("password");
    expect(confirm.type).toBe("password");

    const [showNew, showConfirm] = screen.getAllByRole("button", { name: "Show password" });
    fireEvent.click(showNew);
    fireEvent.click(showConfirm);

    // After toggling, both inputs become text inputs.
    const textInputs = Array.from(
      container.querySelectorAll('input[type="text"]'),
    ) as HTMLInputElement[];
    expect(textInputs).toHaveLength(2);
    expect(screen.getAllByRole("button", { name: "Hide password" })).toHaveLength(2);
  });

  it("toggles the logout-from-all-devices switch", () => {
    renderForm();
    const toggle = screen.getByRole("switch");
    expect(toggle).toHaveAttribute("data-state", "checked");
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("data-state", "unchecked");
  });

  it("shows the authenticating state driven by the animation phase", () => {
    h.animCtx = {
      phase: "submitting",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn(),
      failAnimation: vi.fn(),
      resetAnimation: vi.fn(),
      setPanelIdleSlot: vi.fn(),
    };
    renderForm();
    expect(screen.getByText("Resetting…")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /resetting/i })).toBeDisabled();
  });

  it("resets a failed animation when the user edits the form", () => {
    const resetAnimation = vi.fn();
    h.animCtx = {
      phase: "failed",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn(),
      failAnimation: vi.fn(),
      resetAnimation,
      setPanelIdleSlot: vi.fn(),
    };
    const { container } = renderForm();
    const [password] = passwordInputs(container);
    fireEvent.input(password, { target: { value: "abc" } });
    expect(resetAnimation).toHaveBeenCalled();
  });

  it("injects the password strength checker into the animation panel slot", () => {
    const setPanelIdleSlot = vi.fn();
    h.animCtx = {
      phase: "idle",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn(),
      failAnimation: vi.fn(),
      resetAnimation: vi.fn(),
      setPanelIdleSlot,
    };
    renderForm();
    expect(setPanelIdleSlot).toHaveBeenCalled();
  });

  const fillValidPasswords = (container: HTMLElement) => {
    const [password, confirm] = passwordInputs(container);
    fireEvent.change(password, { target: { value: "Passw0rd!" } });
    fireEvent.change(confirm, { target: { value: "Passw0rd!" } });
  };

  it("submits the reset and navigates to the success page", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const { container } = renderForm();
    fillValidPasswords(container);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    await vi.waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ code: "reset-code", password: "Passw0rd!", tenantId: "tenant-1" }),
      ),
    );
    await vi.waitFor(() =>
      expect(h.navigateMock).toHaveBeenCalledWith("/reset-password-success"),
    );
  });

  it("shows the server error and resets the captcha when the reset fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "Reset link expired" });
    const { container } = renderForm();
    fillValidPasswords(container);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    expect(await screen.findByText("Reset link expired")).toBeInTheDocument();
    expect(h.resetCaptcha).toHaveBeenCalled();
    expect(h.navigateMock).not.toHaveBeenCalled();
  });

  it("surfaces a mapped error when the mutation throws", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { password: "Too weak" } });
    const { container } = renderForm();
    fillValidPasswords(container);
    fireEvent.submit(container.querySelector("form") as HTMLFormElement);
    expect(await screen.findByText("Too weak")).toBeInTheDocument();
  });
});
