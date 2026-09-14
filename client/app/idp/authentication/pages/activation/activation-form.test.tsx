import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  mutateAsync: vi.fn(),
  revalidate: vi.fn(),
  resetCaptcha: vi.fn(),
  animCtx: null as Record<string, unknown> | null,
  oidcUiConfig: undefined as unknown,
  captchaCode: "",
  captchaEnabled: false,
}));

vi.mock("react-router", () => ({ useNavigate: () => h.navigateMock }));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountActivation: vi.fn(() => ({ isPending: false, mutateAsync: h.mutateAsync })),
  useAccountActivationCodeExpiration: vi.fn(() => ({ mutateAsync: h.revalidate })),
}));
vi.mock("@blocks-idp/authentication/components/login-return-link", () => ({
  LoginReturnLink: ({ children }: { children: React.ReactNode }) => <a href="/login">{children}</a>,
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: h.captchaCode, reset: h.resetCaptcha })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({
    data: h.oidcUiConfig,
    captchaEnabled: h.captchaEnabled,
  })),
}));
vi.mock("../../components/password-strength-checker/password-strength-checker", () => ({
  PasswordStrengthChecker: () => null,
}));
vi.mock("../oidc/oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => h.animCtx) }));

import { ActivationForm } from "./activation-form";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

const passwordInputs = (container: HTMLElement) =>
  Array.from(container.querySelectorAll('input[type="password"]')) as HTMLInputElement[];

beforeEach(() => {
  vi.clearAllMocks();
  h.animCtx = null;
  h.captchaCode = "";
  h.captchaEnabled = false;
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
  h.mutateAsync.mockResolvedValue({ isSuccess: true });
  h.revalidate.mockResolvedValue({ isSuccess: false, status: "Expired" });
});

const fillNames = () => {
  fireEvent.change(screen.getByPlaceholderText("First name"), { target: { value: "Grace" } });
  fireEvent.change(screen.getByPlaceholderText("Last name"), { target: { value: "Hopper" } });
};

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

  it("renders tenant-defined activation labels", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          activation: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.activation,
            passwordLabel: "Create passphrase",
            confirmPasswordLabel: "Confirm passphrase",
            submitButton: "Enable account",
          },
        },
      },
    };
    render(<ActivationForm code="activation-code" tenantId="tenant-1" />);
    expect(screen.getByText("Create passphrase")).toBeInTheDocument();
    expect(screen.getByText("Confirm passphrase")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Enable account/ })).toBeInTheDocument();
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

  describe("when the tenant turns the activation password step off", () => {
    const renderAuto = (props: Record<string, unknown> = {}) =>
      render(
        <ActivationForm
          code="activation-code"
          tenantId="tenant-1"
          collectPassword={false}
          {...props}
        />,
      );

    it("asks for nothing and activates on arrival", async () => {
      const { container } = renderAuto();

      expect(screen.queryByText("First Name")).not.toBeInTheDocument();
      expect(screen.queryByText("Last Name")).not.toBeInTheDocument();
      expect(passwordInputs(container)).toHaveLength(0);
      expect(screen.queryByRole("button", { name: /activate/i })).not.toBeInTheDocument();

      await vi.waitFor(() =>
        expect(h.mutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({
            code: "activation-code",
            tenantId: "tenant-1",
            password: undefined,
            firstName: undefined,
            lastName: undefined,
          }),
        ),
      );
      await vi.waitFor(() =>
        expect(h.navigateMock).toHaveBeenCalledWith(
          expect.stringContaining("/oidc/activate-success"),
        ),
      );
    });

    it("activates once however many times the effect re-runs", async () => {
      const { rerender } = renderAuto();
      await vi.waitFor(() => expect(h.mutateAsync).toHaveBeenCalledTimes(1));

      rerender(
        <ActivationForm code="activation-code" tenantId="tenant-1" collectPassword={false} />,
      );
      rerender(
        <ActivationForm code="activation-code" tenantId="tenant-1" collectPassword={false} />,
      );

      await vi.waitFor(() => expect(h.mutateAsync).toHaveBeenCalledTimes(1));
    });

    it("waits for the captcha, then activates without asking for a button press", async () => {
      h.captchaEnabled = true;
      const { rerender } = renderAuto();

      expect(h.mutateAsync).not.toHaveBeenCalled();
      expect(
        screen.getByText("Confirm you are not a robot to finish activating your account."),
      ).toBeInTheDocument();

      h.captchaCode = "solved-captcha";
      rerender(
        <ActivationForm code="activation-code" tenantId="tenant-1" collectPassword={false} />,
      );

      await vi.waitFor(() =>
        expect(h.mutateAsync).toHaveBeenCalledWith(
          expect.objectContaining({ captchaCode: "solved-captcha" }),
        ),
      );
    });

    it("keeps a solved captcha instead of discarding it for unmet password rules", () => {
      h.captchaEnabled = true;
      h.captchaCode = "solved-captcha";

      renderAuto();

      expect(h.resetCaptcha).not.toHaveBeenCalled();
    });

    it("treats an already-active account as success rather than a failure", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { code: "Expired" } });
      h.revalidate.mockResolvedValue({ isSuccess: false, status: "AlreadyActivated" });

      renderAuto();

      await vi.waitFor(() =>
        expect(h.navigateMock).toHaveBeenCalledWith(
          expect.stringContaining("/oidc/activate-success"),
          { replace: true },
        ),
      );
    });

    it("surfaces the failure when the account is not already active", async () => {
      h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { code: "Link is dead" } });
      h.revalidate.mockResolvedValue({ isSuccess: false, status: "Invalid" });

      renderAuto();

      expect(await screen.findByText("Link is dead")).toBeInTheDocument();
      expect(screen.getByText("Back to login")).toBeInTheDocument();
      expect(h.navigateMock).not.toHaveBeenCalledWith(
        expect.stringContaining("/oidc/activate-success"),
      );
    });
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
