import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  captchaCode: "captcha-123",
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "site-key" }));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountRecover: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: h.captchaCode, reset: h.resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));

import { ForgotPasswordForm } from "./forgot-password-form";

const renderForm = () =>
  render(
    <MemoryRouter>
      <ForgotPasswordForm />
    </MemoryRouter>,
  );

const fillValidEmail = async () => {
  fireEvent.input(screen.getByPlaceholderText("name@company.com"), {
    target: { value: "user@example.com" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.captchaCode = "captcha-123";
});

describe("ForgotPasswordForm", () => {
  it("renders the reset heading and back-to-login link", () => {
    renderForm();
    expect(screen.getByText("Reset Password")).toBeInTheDocument();
    expect(screen.getByText("Back to login")).toBeInTheDocument();
  });

  it("submits the recovery request and navigates on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    await fillValidEmail();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Send Recovery Link/ })).toBeEnabled(),
    );
    fireEvent.submit(screen.getByRole("button", { name: /Send Recovery Link/ }).closest("form")!);
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ email: "user@example.com", captchaCode: "captcha-123" }),
      ),
    );
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/forgot-email-sent?email=user@example.com"),
    );
  });

  it("shows the server error and resets the captcha when recovery fails", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { email: "No such account" } });
    renderForm();
    await fillValidEmail();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Send Recovery Link/ })).toBeEnabled(),
    );
    fireEvent.submit(screen.getByRole("button", { name: /Send Recovery Link/ }).closest("form")!);
    await waitFor(() => expect(screen.getByText("No such account")).toBeInTheDocument());
    expect(h.resetCaptcha).toHaveBeenCalled();
  });
});
