import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigate: vi.fn(),
  setAuthenticated: vi.fn(),
  setTokens: vi.fn(),
  mutateAsync: vi.fn(),
  isPending: false,
  resetCaptcha: vi.fn(),
  showError: vi.fn(),
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "site-key" }));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useSigninByEmail: () => ({ isPending: h.isPending, mutateAsync: h.mutateAsync }),
}));
vi.mock("@seliseblocks/genesis-os", () => ({
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

  it("signs in and navigates to the profile page on success", async () => {
    h.mutateAsync.mockResolvedValue({ mfa_required: false });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ username: "user@example.com", password: "secret" }),
      ),
    );
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    await waitFor(() => expect(h.navigate).toHaveBeenCalledWith("/app/profile"));
  });

  // The challenge is a 200 carrying `error: "mfa_enabled"`, so it resolves rather
  // than throwing. Previously this asserted `enable_mfa`/`mfaId`/`mfaType`, keys the
  // server never emitted -- the branch could not fire against a real response.
  it("redirects to the mfa-check page when mfa is required", async () => {
    h.mutateAsync.mockResolvedValue({
      error: "mfa_enabled",
      error_description: "Mfa code required",
      mfa_required: true,
      mfa_id: "m1",
      mfa_type: 1,
    });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/mfa-check?mfa_id=m1&mfa_type=1"),
    );
    expect(h.setAuthenticated).not.toHaveBeenCalled();
  });

  it("url-encodes the mfa_id it carries into the mfa-check url", async () => {
    h.mutateAsync.mockResolvedValue({
      error: "mfa_enabled",
      mfa_required: true,
      mfa_id: "m/1 +2",
      mfa_type: 2,
    });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith("/mfa-check?mfa_id=m%2F1%20%2B2&mfa_type=2"),
    );
  });

  it("shows an error toast when signin fails", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { error: "invalid_grant", error_description: "Bad login" } });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "Bad login" }));
  });

  it("navigates to the oidc permission page on success in oidc mode", async () => {
    h.mutateAsync.mockResolvedValue({ mfa_required: false });
    renderForm({ mode: "oidc", oidcContext: { clientId: "c1", scope: "openid" } });
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith(
        expect.stringContaining("/oidc/permission?"),
      ),
    );
  });

  it("redirects to the oidc mfa-check page in oidc mode", async () => {
    h.mutateAsync.mockResolvedValue({
      error: "mfa_enabled",
      mfa_required: true,
      mfa_id: "m2",
      mfa_type: 2,
    });
    renderForm({ mode: "oidc", oidcContext: { clientId: "c1" } });
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() =>
      expect(h.navigate).toHaveBeenCalledWith(
        expect.stringContaining("/oidc/mfa-check?mfa_id=m2&mfa_type=2"),
      ),
    );
  });

  it("shows the captcha when the server requires it", async () => {
    h.mutateAsync.mockRejectedValue({
      errors: { error: "captcha_enabled", captcha_site_key: "sk-1", error_description: "Captcha needed" },
    });
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(h.resetCaptcha).toHaveBeenCalled());
    await waitFor(() => expect(screen.getByTestId("captcha")).toBeInTheDocument());
  });

  it("shows a generic error toast for a non-structured failure", async () => {
    h.mutateAsync.mockRejectedValue("kaput");
    renderForm();
    fillCredentials();
    fireEvent.click(screen.getByRole("button", { name: "Log in" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" }));
  });
});
