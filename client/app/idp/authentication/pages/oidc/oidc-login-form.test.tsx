import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  resetCaptcha: vi.fn(),
  selectOidcAccount: vi.fn(),
  loginOption: undefined as unknown,
  oidcUiConfig: undefined as unknown,
  captchaEnabled: false,
  signUpSetting: { isSignUpEnable: false } as unknown,
  captchaCode: "",
  animCtx: null as Record<string, unknown> | null,
  accountSelectorProps: null as Record<string, unknown> | null,
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: vi.fn(), setTokens: vi.fn() })),
}));
vi.mock("@/components/captcha", () => ({ Captcha: () => <div data-testid="captcha" /> }));
vi.mock("./oidc-account-selector", () => ({
  OidcAccountSelector: (props: Record<string, unknown>) => {
    h.accountSelectorProps = props;
    const accounts = props.accounts as { user_id: string; email: string }[];
    return (
      <div data-testid="account-selector">
        {accounts.map((a) => (
          <button
            key={a.user_id}
            onClick={() => (props.onAccountSelect as (x: unknown) => void)(a)}
          >
            pick-{a.email}
          </button>
        ))}
      </div>
    );
  },
}));
vi.mock("./oidc-auth-shell", () => ({ useOidcAuthAnimation: vi.fn(() => h.animCtx) }));
vi.mock("@blocks-idp/authentication/pages/login/sso-signin", () => ({
  SsoSignin: () => <div data-testid="sso-signin" />,
}));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { selectOidcAccount: (...args: unknown[]) => h.selectOidcAccount(...args) },
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: vi.fn(() => ({ data: h.loginOption })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: h.oidcUiConfig, captchaEnabled: h.captchaEnabled })),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: vi.fn(() => ({ data: h.signUpSetting })),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: h.captchaCode, reset: h.resetCaptcha })),
}));

import { OidcLoginForm } from "./oidc-login-form";

const renderForm = (props: Partial<React.ComponentProps<typeof OidcLoginForm>> = {}) =>
  render(
    <MemoryRouter>
      <OidcLoginForm
        clientId={props.clientId ?? "client-1"}
        redirectUri={props.redirectUri ?? "https://app.example.com/callback"}
        tenantId={props.tenantId ?? "tenant-1"}
        codeChallenge={props.codeChallenge}
        codeChallengeMethod={props.codeChallengeMethod}
        scope={props.scope}
        state={props.state}
        nonce={props.nonce}
      />
    </MemoryRouter>,
  );

const jsonResponse = (
  status: number,
  body: unknown,
  { ok, contentType = "application/json" }: { ok?: boolean; contentType?: string } = {},
) => ({
  ok: ok ?? (status >= 200 && status < 300),
  status,
  headers: { get: () => contentType },
  json: async () => body,
});

const fillValid = () => {
  fireEvent.change(screen.getByPlaceholderText("name@company.com"), {
    target: { value: "jane@company.com" },
  });
  fireEvent.change(screen.getByPlaceholderText("••••••••"), {
    target: { value: "s3cret-pass" },
  });
};

const originalLocation = window.location;

beforeEach(() => {
  vi.clearAllMocks();
  h.loginOption = undefined;
  h.oidcUiConfig = undefined;
  h.captchaEnabled = false;
  h.signUpSetting = { isSignUpEnable: false };
  h.captchaCode = "";
  h.animCtx = null;
  h.accountSelectorProps = null;
  sessionStorage.clear();
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: { href: "" },
  });
});

afterEach(() => {
  vi.unstubAllGlobals();
  Object.defineProperty(window, "location", {
    configurable: true,
    writable: true,
    value: originalLocation,
  });
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
  });

  it("posts credentials to the OIDC login endpoint on a valid submit", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(401, { error: "invalid_credentials" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/oidc/login");
    const body = JSON.parse(options.body as string);
    expect(body.username).toBe("jane@company.com");
    expect(body.password).toBe("s3cret-pass");
    expect(body.client_id).toBe("client-1");
    // No codeChallenge prop -> a PKCE verifier is generated and persisted.
    expect(sessionStorage.getItem("oidc-code-verifier")).toBeTruthy();
    expect(body.code_challenge).toBeTruthy();

    expect(
      await screen.findByText("Invalid email or password. Please try again."),
    ).toBeInTheDocument();
  });

  it("forwards a caller-supplied code challenge verbatim without touching sessionStorage", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(401, { error: "invalid_credentials" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm({ codeChallenge: "external-challenge", codeChallengeMethod: "S256" });
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string);
    expect(body.code_challenge).toBe("external-challenge");
    expect(sessionStorage.getItem("oidc-code-verifier")).toBeNull();
  });

  it("redirects to the returned redirect_uri on a successful login", async () => {
    h.animCtx = {
      phase: "idle",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn().mockResolvedValue(undefined),
      failAnimation: vi.fn().mockResolvedValue(undefined),
      resetAnimation: vi.fn(),
    };
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse(200, { redirect_uri: "https://app.example.com/next" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(window.location.href).toBe("https://app.example.com/next"));
    expect(h.animCtx.startAnimation).toHaveBeenCalled();
  });

  it("navigates to the MFA check screen when the backend signals mfa_enabled", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse(200, { error: "mfa_enabled", mfa_id: "mfa-9", user_mfa: "authenticator" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(h.navigateMock).toHaveBeenCalled());
    const dest = h.navigateMock.mock.calls[0][0] as string;
    expect(dest).toContain("/oidc/mfa-check");
    expect(dest).toContain("mfa_id=mfa-9");
    expect(dest).toContain("mfa_type=1");
  });

  it("shows the account selector when account selection is required", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(
        200,
        {
          status: "account_selection_required",
          accounts: [
            { user_id: "u1", tenant_id: "t1", email: "a@x.com" },
            { user_id: "u2", tenant_id: "t2", email: "b@x.com" },
          ],
        },
        { ok: false },
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByTestId("account-selector")).toBeInTheDocument();
    expect(screen.getByText("pick-a@x.com")).toBeInTheDocument();
  });

  it("redirects when an account is selected successfully", async () => {
    h.selectOidcAccount.mockResolvedValue({ redirect_url: "https://app.example.com/consent" });
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(
        200,
        { status: "account_selection_required", accounts: [{ user_id: "u1", tenant_id: "t1", email: "a@x.com" }] },
        { ok: false },
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    fireEvent.click(await screen.findByText("pick-a@x.com"));
    await waitFor(() => expect(window.location.href).toBe("https://app.example.com/consent"));
    expect(h.selectOidcAccount).toHaveBeenCalledWith(
      expect.objectContaining({ userId: "u1", tenantId: "t1", clientId: "client-1" }),
    );
  });

  it("surfaces an error when the selected account has no redirect url", async () => {
    h.selectOidcAccount.mockResolvedValue({});
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(
        200,
        { status: "account_selection_required", accounts: [{ user_id: "u1", tenant_id: "t1", email: "a@x.com" }] },
        { ok: false },
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));
    fireEvent.click(await screen.findByText("pick-a@x.com"));

    await waitFor(() => expect(h.selectOidcAccount).toHaveBeenCalled());
    expect(window.location.href).toBe("");
  });

  it("surfaces the error description when account selection rejects with errors", async () => {
    h.selectOidcAccount.mockRejectedValue({ errors: { error_description: "no access here" } });
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(
        200,
        { status: "account_selection_required", accounts: [{ user_id: "u1", tenant_id: "t1", email: "a@x.com" }] },
        { ok: false },
      ),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));
    fireEvent.click(await screen.findByText("pick-a@x.com"));

    await waitFor(() => expect(h.selectOidcAccount).toHaveBeenCalled());
    expect(window.location.href).toBe("");
  });

  it("shows a locked-account message for account_locked", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(403, { error: "account_locked" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(
      await screen.findByText(/Your account is locked/),
    ).toBeInTheDocument();
  });

  it("shows the activation screen for account_not_verified and can return to login", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(403, { error: "account_not_verified" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText("Account Not Verified")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Activate Account" }));
    expect(window.location.href).toContain("/oidc/activation");

    fireEvent.click(screen.getByRole("button", { name: "Back to Login" }));
    expect(await screen.findByText("Work Email")).toBeInTheDocument();
  });

  it("prompts for captcha and reveals the widget when captcha_enabled is returned", async () => {
    h.captchaEnabled = true;
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(400, { error: "captcha_enabled" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText(/Captcha verification is required/)).toBeInTheDocument();
    expect(h.resetCaptcha).toHaveBeenCalled();
    expect(await screen.findByTestId("captcha")).toBeInTheDocument();
  });

  it("shows a captcha failure message for captcha_invalid", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(400, { error: "captcha_invalid" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText("Captcha verification failed. Please try again.")).toBeInTheDocument();
  });

  it("shows the server-provided description for an unrecognised error", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse(400, { error: "weird", error_description: "Boom happened" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText("Boom happened")).toBeInTheDocument();
  });

  it("shows a generic HTTP error when the response is not JSON", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(jsonResponse(500, null, { ok: false, contentType: "text/html" }));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByText("Server error (HTTP 500)")).toBeInTheDocument();
  });

  it("shows an unexpected-error message when fetch rejects", async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error("network down"));
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(
      await screen.findByText("An unexpected error occurred during login. Please try again."),
    ).toBeInTheDocument();
  });

  it("toggles password visibility", () => {
    renderForm();
    const passwordInput = screen.getByPlaceholderText("••••••••") as HTMLInputElement;
    expect(passwordInput.type).toBe("password");
    fireEvent.click(screen.getByRole("button", { name: "Show password" }));
    expect(passwordInput.type).toBe("text");
    fireEvent.click(screen.getByRole("button", { name: "Hide password" }));
    expect(passwordInput.type).toBe("password");
  });

  it("renders the social sign-in block when SSO options exist", () => {
    h.loginOption = { ssoInfo: [{ provider: "google" }] };
    renderForm();
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
    expect(screen.getByText("or")).toBeInTheDocument();
  });

  it("renders the sign-up link when self sign-up is enabled", () => {
    h.signUpSetting = { isSignUpEnable: true };
    renderForm();
    expect(screen.getByText("Create an account")).toBeInTheDocument();
  });
});
