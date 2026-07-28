import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  captchaEnabled: false,
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "" }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountRecover: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "", reset: h.resetCaptcha }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: undefined, captchaEnabled: h.captchaEnabled }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));

import { OIDCForgotPasswordForm } from "./oidc-forgot-password-form";

const renderForm = () =>
  render(
    <MemoryRouter>
      <OIDCForgotPasswordForm />
    </MemoryRouter>,
  );

const fillEmail = () =>
  fireEvent.input(screen.getByPlaceholderText("name@company.com"), {
    target: { value: "user@example.com" },
  });

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.captchaEnabled = false;
});

describe("OIDCForgotPasswordForm", () => {
  it("renders the reset heading and back-to-login link", () => {
    renderForm();
    expect(screen.getByText("Reset Password")).toBeInTheDocument();
    expect(screen.getByText("Back to login")).toBeInTheDocument();
  });

  it("submits recovery and navigates to the oidc confirmation page", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fillEmail();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Send Recovery Link/ })).toBeEnabled(),
    );
    fireEvent.submit(screen.getByRole("button", { name: /Send Recovery Link/ }).closest("form")!);
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/oidc/forgot-email-sent?email=user@example.com"),
    );
  });

  it("shows a server error when recovery fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: ["No account"] });
    renderForm();
    fillEmail();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Send Recovery Link/ })).toBeEnabled(),
    );
    fireEvent.submit(screen.getByRole("button", { name: /Send Recovery Link/ }).closest("form")!);
    await waitFor(() => expect(screen.getByText("No account")).toBeInTheDocument());
  });
});
