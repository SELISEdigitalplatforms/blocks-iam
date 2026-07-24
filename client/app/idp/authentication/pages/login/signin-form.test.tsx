import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  showError: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "site-key" }));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSigninByEmail: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useAuthStore: () => ({ setAuthenticated: h.setAuthenticated, setTokens: h.setTokens }),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => h.showError(a) }));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: () => ({ captcha: {}, code: "", reset: h.resetCaptcha }),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (p: string) => `/oidc${p}`,
  getCurrentOIDCParams: () => new URLSearchParams(),
}));

import { SigninForm } from "./signin-form";

const renderForm = (props = {}) =>
  render(
    <MemoryRouter>
      <SigninForm {...props} />
    </MemoryRouter>,
  );

const fillCredentials = () => {
  fireEvent.input(screen.getByPlaceholderText("Enter your email"), {
    target: { value: "user@example.com" },
  });
  fireEvent.input(screen.getByPlaceholderText("Enter your password"), {
    target: { value: "secret" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("SigninForm", () => {
  it("renders the email, password fields and forgot-password link", () => {
    renderForm();
    expect(screen.getByPlaceholderText("Enter your email")).toBeInTheDocument();
    expect(screen.getByText("Forgot password?")).toBeInTheDocument();
  });

  it("signs in and navigates to the console on success", async () => {
    h.mutateAsync.mockResolvedValue({ enable_mfa: false });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ username: "user@example.com", password: "secret" }),
      ),
    );
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    await waitFor(() => expect(h.navigate).toHaveBeenCalledWith("/app/console"));
  });

  it("redirects to the mfa-check page when mfa is required", async () => {
    h.mutateAsync.mockResolvedValue({ enable_mfa: true, mfaId: "m1", mfaType: "totp" });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/mfa-check?mfa_id=m1&mfa_type=totp"),
    );
  });

  it("shows an error toast when signin fails", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { error: "invalid_grant", error_description: "Bad login" } });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "Bad login" }));
  });
});
