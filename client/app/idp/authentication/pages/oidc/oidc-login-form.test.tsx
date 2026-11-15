import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  resetCaptcha: vi.fn(),
  selectOidcAccount: vi.fn(),
  loginOption: undefined as unknown,
  loginOptionLoading: false,
  signUpSettingLoading: false,
  oidcUiConfig: undefined as unknown,
  captchaEnabled: false,
  signUpSetting: { isSignUpEnable: false } as unknown,
  captchaCode: "",
  animCtx: null as Record<string, unknown> | null,
  accountSelectorProps: null as Record<string, unknown> | null,
  startSso: vi.fn(),
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
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
  SsoSignin: ({ disabled }: { disabled?: boolean }) => (
    <div data-testid="sso-signin" data-disabled={String(!!disabled)} />
  ),
}));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { selectOidcAccount: (...args: unknown[]) => h.selectOidcAccount(...args) },
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth", () => ({
  useGetLoginOptions: vi.fn(() => ({ data: h.loginOption, isLoading: h.loginOptionLoading })),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: vi.fn(() => ({ data: h.oidcUiConfig, captchaEnabled: h.captchaEnabled })),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: vi.fn(() => ({ data: h.signUpSetting, isLoading: h.signUpSettingLoading })),
}));
vi.mock("@blocks-idp/authentication/utils/start-oidc-social-sign-in", () => ({
  startOidcSocialSignIn: (...args: unknown[]) => h.startSso(...args),
}));
vi.mock("@blocks-idp/captcha/hooks/use-captcha", () => ({
  useCaptcha: vi.fn(() => ({ captcha: {}, code: h.captchaCode, reset: h.resetCaptcha })),
}));

import { OidcLoginForm } from "./oidc-login-form";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

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
        initialError={props.initialError}
        returnUrl={props.returnUrl}
        autoStartSingleProvider={props.autoStartSingleProvider}
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
  h.loginOptionLoading = false;
  h.signUpSettingLoading = false;
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
  h.captchaEnabled = false;
  h.signUpSetting = { isSignUpEnable: false };
  h.captchaCode = "";
  h.animCtx = null;
  h.accountSelectorProps = null;
  h.startSso.mockResolvedValue(true);
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

  it("renders tenant-defined login labels and actions", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          login: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages.login,
            emailLabel: "Corporate Email",
            passwordLabel: "Secret Phrase",
            forgotPasswordLink: "Recover access",
            submitButton: "Continue securely",
          },
        },
      },
    };
    renderForm();
    expect(screen.getByText("Corporate Email")).toBeInTheDocument();
    expect(screen.getByText("Secret Phrase")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Recover access" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Continue securely/ })).toBeInTheDocument();
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

  describe("redirect target after a successful login", () => {
    const APP_URL = "com.klaxai.app://login/callback?code=abc&state=xyz";
    let assignments: string[];

    const animCtx = () => ({
      phase: "idle",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn().mockResolvedValue(undefined),
      failAnimation: vi.fn().mockResolvedValue(undefined),
      resetAnimation: vi.fn(),
    });

    const submitWith = (body: unknown) => {
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(200, body)));
      renderForm();
      fillValid();
      fireEvent.click(screen.getByRole("button", { name: /login/i }));
    };

    beforeEach(() => {
      assignments = [];
      Object.defineProperty(window, "location", {
        configurable: true,
        value: {
          get href() {
            return assignments[assignments.length - 1] ?? "";
          },
          set href(value: string) {
            assignments.push(value);
          },
        },
      });
    });

    it("hands an app-scheme redirect_uri to the app once and replaces the form (H6, H7)", async () => {
      h.animCtx = animCtx();
      submitWith({ redirect_uri: APP_URL });

      const link = await screen.findByRole("link", { name: "Return to the app" });
      expect(link).toHaveAttribute("href", APP_URL);
      await waitFor(() => expect(link).toHaveFocus());
      expect(screen.getByRole("heading", { name: "Signed in" })).toBeInTheDocument();
      expect(screen.queryByPlaceholderText("name@company.com")).not.toBeInTheDocument();
      expect(assignments).toEqual([APP_URL]);
      // The shell would swap the hand-off for its success screen once this resolved.
      expect(h.animCtx.succeedAnimation).not.toHaveBeenCalled();
      expect(h.animCtx.resetAnimation).toHaveBeenCalled();
    });

    it("keeps the success animation and a single navigation for a web redirect_uri (H3)", async () => {
      h.animCtx = animCtx();
      submitWith({ redirect_uri: "https://app.example.com/next" });

      await waitFor(() => expect(assignments).toEqual(["https://app.example.com/next"]));
      expect(h.animCtx.succeedAnimation).toHaveBeenCalled();
      expect(screen.queryByRole("link", { name: "Return to the app" })).not.toBeInTheDocument();
    });

    it.each(["javascript:alert(1)", "myapp://callback", "com.example.app://cb#frag"])(
      "refuses %s with the no-redirect-target error and does not navigate (C10)",
      async (redirectUri) => {
        h.animCtx = animCtx();
        submitWith({ redirect_uri: redirectUri });

        expect(
          await screen.findByText("Login succeeded but no redirect target was provided"),
        ).toBeInTheDocument();
        expect(h.animCtx.failAnimation).toHaveBeenCalledWith(
          "Login succeeded but no redirect target was provided",
        );
        expect(assignments).toEqual([]);
        expect(screen.queryByRole("link", { name: "Return to the app" })).not.toBeInTheDocument();
      },
    );

    it("shows the hand-off without an animation context as well", async () => {
      submitWith({ redirect_uri: APP_URL });
      expect(await screen.findByRole("link", { name: "Return to the app" })).toBeInTheDocument();
      expect(assignments).toEqual([APP_URL]);
    });
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

  it("shows the organization disabled message in the alert slot for organization_disabled", async () => {
    h.animCtx = {
      phase: "idle",
      startAnimation: vi.fn(),
      succeedAnimation: vi.fn().mockResolvedValue(undefined),
      failAnimation: vi.fn().mockResolvedValue(undefined),
      resetAnimation: vi.fn(),
    };
    const message = "Your organization is disabled. Please contact your administrator or support.";
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(403, { error: "organization_disabled", error_description: "Server text" }),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    expect(await screen.findByRole("alert")).toHaveTextContent(message);
    expect(screen.queryByText("Server text")).not.toBeInTheDocument();
    expect(h.animCtx.failAnimation).toHaveBeenCalledWith(message);
    expect(window.location.href).toBe("");
  });

  it("still follows the redirect when a successful login carries org_status disabled", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse(200, { redirect_uri: "https://app.example.com/next", org_status: "disabled" }),
    );
    vi.stubGlobal("fetch", fetchMock);

    renderForm();
    fillValid();
    fireEvent.click(screen.getByRole("button", { name: /login/i }));

    await waitFor(() => expect(window.location.href).toBe("https://app.example.com/next"));
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

  it("asks for the options of this application", async () => {
    const { useGetLoginOptions } = await import("@blocks-idp/authentication/hooks/use-auth");
    renderForm();
    expect(useGetLoginOptions).toHaveBeenCalledWith(expect.anything(), true, expect.any(String));
  });

  it("hides the password form when the application allows social only", () => {
    h.loginOption = { allowedGrantTypes: ["authorization_code", "social"], ssoInfo: [{ provider: "google" }] };
    renderForm();
    expect(screen.queryByPlaceholderText("••••••••")).not.toBeInTheDocument();
    expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
    expect(screen.queryByText("or")).not.toBeInTheDocument();
  });

  it("keeps the password form while the options have no grant list", () => {
    h.loginOption = { ssoInfo: [] };
    renderForm();
    expect(screen.getByPlaceholderText("••••••••")).toBeInTheDocument();
  });

  describe("before the server has said which methods are allowed", () => {
    it("draws a skeleton, not the password form, while the login options load", () => {
      h.loginOptionLoading = true;
      renderForm();
      expect(screen.getByTestId("oidc-login-form-skeleton")).toBeInTheDocument();
      expect(screen.getByRole("status")).toHaveTextContent("Loading sign-in options…");
      expect(screen.queryByPlaceholderText("••••••••")).not.toBeInTheDocument();
      expect(screen.queryByTestId("sso-signin")).not.toBeInTheDocument();
      expect(screen.queryByTestId("no-sign-in-method")).not.toBeInTheDocument();
    });

    it("draws a skeleton, not a late sign-up link, while the sign-up setting loads", () => {
      h.signUpSettingLoading = true;
      h.loginOption = { allowedGrantTypes: ["social"], ssoInfo: [{ provider: "google" }] };
      renderForm();
      expect(screen.getByTestId("oidc-login-form-skeleton")).toBeInTheDocument();
      expect(screen.queryByTestId("sso-signin")).not.toBeInTheDocument();
      expect(screen.queryByText("Create an account")).not.toBeInTheDocument();
    });

    it("goes straight to the configured methods once both answers are in", () => {
      h.loginOption = { allowedGrantTypes: ["social"], ssoInfo: [{ provider: "google" }] };
      h.signUpSetting = { isSignUpEnable: true };
      renderForm();
      expect(screen.queryByTestId("oidc-login-form-skeleton")).not.toBeInTheDocument();
      expect(screen.queryByPlaceholderText("••••••••")).not.toBeInTheDocument();
      expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
      expect(screen.getByText("Create an account")).toBeInTheDocument();
    });

    it("does not start a provider before both answers are in", () => {
      h.loginOptionLoading = true;
      h.loginOption = undefined;
      renderForm({ autoStartSingleProvider: true });
      expect(h.startSso).not.toHaveBeenCalled();
    });

    it("falls back to the password form when the options fail to load", () => {
      // Settled with no data (the request failed): the server still refuses a disabled method.
      h.loginOption = undefined;
      renderForm();
      expect(screen.queryByTestId("oidc-login-form-skeleton")).not.toBeInTheDocument();
      expect(screen.getByPlaceholderText("••••••••")).toBeInTheDocument();
    });
  });

  it("says so when no sign-in method is available", () => {
    h.loginOption = { allowedGrantTypes: ["authorization_code"], ssoInfo: null };
    renderForm();
    expect(screen.getByTestId("no-sign-in-method")).toHaveTextContent(
      "No sign-in method is available for this application.",
    );
    expect(screen.queryByPlaceholderText("••••••••")).not.toBeInTheDocument();
  });

  it("renders the sign-up link when self sign-up is enabled", () => {
    h.signUpSetting = { isSignUpEnable: true };
    renderForm();
    expect(screen.getByText("Create an account")).toBeInTheDocument();
  });

  describe("starting the only sign-in method for the user", () => {
    const onlyProvider = (provider: string) => ({
      allowedGrantTypes: ["authorization_code", "social"],
      ssoInfo: [{ provider, clientId: `${provider}-cid` }],
    });

    it.each([
      ["microsoft", "Redirecting to Microsoft…"],
      ["google", "Redirecting to Google…"],
      ["github", "Redirecting to GitHub…"],
      ["linkedin", "Redirecting to LinkedIn…"],
      ["apple", "Redirecting to Apple…"],
      ["ownsso", "Redirecting to your organization's sign-in…"],
    ])("starts %s when it is the only option, keeping its button as a fallback", async (provider, status) => {
      h.loginOption = onlyProvider(provider);
      renderForm({ autoStartSingleProvider: true, state: "st-1", nonce: "n-1", scope: "openid" });

      await waitFor(() => expect(h.startSso).toHaveBeenCalledTimes(1));
      expect(h.startSso).toHaveBeenCalledWith({
        provider,
        providerClientId: `${provider}-cid`,
        replace: true,
        oidcContext: expect.objectContaining({
          clientId: "client-1",
          redirectUri: "https://app.example.com/callback",
          tenantId: "tenant-1",
          state: "st-1",
          nonce: "n-1",
          scope: "openid",
        }),
      });
      expect(screen.getByTestId("oidc-auto-start")).toHaveTextContent(status);
      // The button stays in view but cannot start a second sign-in while this one leaves.
      expect(screen.getByTestId("sso-signin")).toHaveAttribute("data-disabled", "true");
    });

    it("leaves the provider buttons usable when nothing is started", () => {
      h.loginOption = onlyProvider("google");
      renderForm({ autoStartSingleProvider: false });
      expect(screen.getByTestId("sso-signin")).toHaveAttribute("data-disabled", "false");
    });

    it.each([
      ["the password form is allowed", { allowedGrantTypes: ["password", "social"], ssoInfo: [{ provider: "google", clientId: "g" }] }, {}],
      ["the grant list is missing (password stays)", { ssoInfo: [{ provider: "google", clientId: "g" }] }, {}],
      [
        "there are two providers",
        { allowedGrantTypes: ["social"], ssoInfo: [{ provider: "google", clientId: "g" }, { provider: "microsoft", clientId: "m" }] },
        {},
      ],
      ["the only provider is one this page cannot show", { allowedGrantTypes: ["social"], ssoInfo: [{ provider: "facebook", clientId: "f" }] }, {}],
      ["no provider is offered", { allowedGrantTypes: ["authorization_code"], ssoInfo: [] }, {}],
      ["the sign-up link is shown", onlyProvider("google"), { signUp: true }],
      ["the user came back with an error", onlyProvider("google"), { initialError: "Sign-in failed at Google." }],
      ["it is a device-flow sign-in", onlyProvider("google"), { returnUrl: "/oidc/device/entry" }],
      ["the caller did not opt in", onlyProvider("google"), { optOut: true }],
    ] as const)("does not start anything when %s", async (_case, loginOption, opts) => {
      h.loginOption = loginOption;
      if ("signUp" in opts) h.signUpSetting = { isSignUpEnable: true };
      renderForm({
        autoStartSingleProvider: !("optOut" in opts),
        initialError: "initialError" in opts ? opts.initialError : undefined,
        returnUrl: "returnUrl" in opts ? opts.returnUrl : undefined,
      });
      // Give a would-be effect its turn before asserting nothing happened.
      await Promise.resolve();
      expect(h.startSso).not.toHaveBeenCalled();
      expect(screen.queryByTestId("oidc-auto-start")).not.toBeInTheDocument();
    });

    it("starts only once per sign-in request, then waits for the click", async () => {
      h.loginOption = onlyProvider("google");
      const first = renderForm({ autoStartSingleProvider: true, state: "st-1", nonce: "n-1" });
      await waitFor(() => expect(h.startSso).toHaveBeenCalledTimes(1));
      first.unmount();

      // Back on the page for the same request (e.g. returned from the provider unfinished).
      renderForm({ autoStartSingleProvider: true, state: "st-1", nonce: "n-1" });
      await Promise.resolve();
      expect(h.startSso).toHaveBeenCalledTimes(1);
      expect(screen.getByTestId("sso-signin")).toBeInTheDocument();
    });

    it("starts again for a new sign-in request", async () => {
      h.loginOption = onlyProvider("google");
      const first = renderForm({ autoStartSingleProvider: true, state: "st-1" });
      await waitFor(() => expect(h.startSso).toHaveBeenCalledTimes(1));
      first.unmount();

      renderForm({ autoStartSingleProvider: true, state: "st-2" });
      await waitFor(() => expect(h.startSso).toHaveBeenCalledTimes(2));
    });

    it("drops the status line and leaves the button when the start fails", async () => {
      h.startSso.mockResolvedValue(false);
      h.loginOption = onlyProvider("google");
      renderForm({ autoStartSingleProvider: true, state: "st-1" });
      await waitFor(() => expect(h.startSso).toHaveBeenCalled());
      await waitFor(() => expect(screen.queryByTestId("oidc-auto-start")).not.toBeInTheDocument());
      expect(screen.getByTestId("sso-signin")).toHaveAttribute("data-disabled", "false");
    });
  });

  describe("an error carried back from a failed SSO round trip", () => {
    const SSO_ERROR =
      "No account exists for this email, and signing up with SSO is turned off.";

    it("states it in a modal rather than a line under the form", async () => {
      h.loginOption = { ssoInfo: [{ provider: "microsoft" }] };

      renderForm({ initialError: SSO_ERROR });

      const dialog = await screen.findByRole("alertdialog");
      expect(dialog).toHaveTextContent(SSO_ERROR);
      expect(dialog).toHaveTextContent("Sign-in Unavailable");
    });

    it("labels the way out with the tenant's own copy", async () => {
      h.oidcUiConfig = {
        captcha: null,
        template: {
          ...OIDC_UI_TEMPLATE_FIXTURE,
          pages: {
            ...OIDC_UI_TEMPLATE_FIXTURE.pages,
            login: { ...OIDC_UI_TEMPLATE_FIXTURE.pages.login, backToLoginButton: "Return to Acme" },
          },
        },
      };

      renderForm({ initialError: SSO_ERROR });

      expect(await screen.findByRole("button", { name: "Return to Acme" })).toBeInTheDocument();
    });

    it("hands the user back to the application that sent them", async () => {
      renderForm({
        initialError: SSO_ERROR,
        redirectUri: "https://app.example.com/login/callback",
      });

      fireEvent.click(await screen.findByRole("button", { name: "Back to Login" }));

      expect(window.location.href).toBe("https://app.example.com/login/callback");
    });

    it("closes the dialog without navigating when the redirect_uri is refused (C11)", async () => {
      renderForm({ initialError: SSO_ERROR, redirectUri: "javascript:alert(1)" });

      fireEvent.click(await screen.findByRole("button", { name: "Back to Login" }));

      await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
      expect(window.location.href).toBe("");
      expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
    });

    it("hands the user back to a native app's redirect_uri too", async () => {
      renderForm({ initialError: SSO_ERROR, redirectUri: "com.example.app:/oauth2redirect" });

      fireEvent.click(await screen.findByRole("button", { name: "Back to Login" }));

      expect(window.location.href).toBe("com.example.app:/oauth2redirect");
    });

    it("cannot be escaped past, since the message still applies to the page behind it", async () => {
      renderForm({ initialError: SSO_ERROR });
      const dialog = await screen.findByRole("alertdialog");

      fireEvent.keyDown(document, { key: "Escape" });

      expect(dialog).toBeInTheDocument();
    });

    it("falls back to revealing the form when there is nowhere to send the user", async () => {
      renderForm({ initialError: SSO_ERROR, redirectUri: "" });

      fireEvent.click(await screen.findByRole("button", { name: "Back to Login" }));

      await waitFor(() => expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument());
      expect(screen.getByPlaceholderText("name@company.com")).toBeInTheDocument();
    });

    it("leaves an ordinary sign-in failure inline", async () => {
      vi.stubGlobal(
        "fetch",
        vi.fn(async () => jsonResponse(400, { error_description: "Invalid credentials" })),
      );

      renderForm();
      fillValid();
      fireEvent.click(screen.getByRole("button", { name: /login/i }));

      await waitFor(() => expect(screen.getByText("Invalid credentials")).toBeInTheDocument());
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    });
  });
});
