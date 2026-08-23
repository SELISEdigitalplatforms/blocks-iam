import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  captchaEnabled: false,
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
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
    await waitFor(() => expect(h.navigate).toHaveBeenCalled());

    const target = h.navigate.mock.calls[0][0] as string;
    expect(target).toContain("/oidc/forgot-email-sent");
    expect(target).toContain("email=user%40example.com");
  });

  /**
   * The confirmation page's "Go to login" can only return the user to their application
   * if the OIDC context survives this navigation. Rebuilding the path from scratch used
   * to drop it, stranding the user on a login page with no application to sign in to.
   */
  it("carries the OIDC context across to the confirmation page", async () => {
    window.history.replaceState(
      {},
      "",
      "/oidc/forgot-password?clientId=client-1&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcallback&state=st-1&tenant_id=tenant-1",
    );

    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderForm();
    fillEmail();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Send Recovery Link/ })).toBeEnabled(),
    );
    fireEvent.submit(screen.getByRole("button", { name: /Send Recovery Link/ }).closest("form")!);
    await waitFor(() => expect(h.navigate).toHaveBeenCalled());

    const target = h.navigate.mock.calls[0][0] as string;
    expect(target).toContain("clientId=client-1");
    expect(target).toContain("redirect_uri=https%3A%2F%2Fapp.example.com%2Fcallback");
    expect(target).toContain("state=st-1");
    expect(target).toContain("tenant_id=tenant-1");
    expect(target).toContain("email=user%40example.com");

    window.history.replaceState({}, "", "/");
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
