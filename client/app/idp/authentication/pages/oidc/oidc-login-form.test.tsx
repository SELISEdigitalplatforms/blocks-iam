import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  resetCaptcha: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: vi.fn(), setTokens: vi.fn() })),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => null }));
vi.mock("./oidc-account-selector", () => ({ OidcAccountSelector: () => null }));
vi.mock("./oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => null) }));
vi.mock("@blocks-idp/authentication/pages/login/sso-signin", () => ({
  SsoSignin: () => null,
}));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { selectOidcAccount: vi.fn() },
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: vi.fn(() => ({ data: undefined })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: undefined, captchaEnabled: false })),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: vi.fn(() => ({ data: { isSignUpEnable: false } })),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: "", reset: h.resetCaptcha })),
}));

import { OidcLoginForm } from "./oidc-login-form";

const renderForm = () =>
  render(
    <MemoryRouter>
      <OidcLoginForm clientId="client-1" redirectUri="https://app.example.com/callback" tenantId="tenant-1" />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("OidcLoginForm", () => {
  it("renders the email + password fields and the login button", () => {
    renderForm();
    expect(screen.getByText("Work Email")).toBeInTheDocument();
    expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
    expect(screen.getByText("Password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /login/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /forgot/i })).toBeInTheDocument();
  });

  it("shows validation errors when submitting empty", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText("Invalid email address")).toBeInTheDocument();
    expect(screen.getByText("Password is required")).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();

    vi.unstubAllGlobals();
  });

  it("posts credentials to the OIDC login endpoint on a valid submit", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      headers: { get: () => "application/json" },
      json: async () => ({ error: "invalid_credentials" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fireEvent.change(screen.getByPlaceholderText("name@company.com"), {
      target: { value: "jane@company.com" },
    });
    fireEvent.change(screen.getByPlaceholderText("••••••••"), {
      target: { value: "s3cret-pass" },
    });
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/oidc/login");
    const body = JSON.parse(options.body as string);
    expect(body.username).toBe("jane@company.com");
    expect(body.password).toBe("s3cret-pass");
    expect(body.client_id).toBe("client-1");

    expect(
      await screen.findByText("Invalid email or password. Please try again."),
    ).toBeInTheDocument();

    vi.unstubAllGlobals();
  });
});
