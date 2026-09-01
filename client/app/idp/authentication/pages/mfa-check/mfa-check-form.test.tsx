import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// ─── Mocks ───────────────────────────────────────────────────────────────────
const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setAuthenticated: vi.fn(),
  resend: vi.fn(),
  // The IdP bundle's own build-time project key -- always the root tenant.
  envTenant: "root-project-key" as string | undefined,
  // The tenant of the login being completed, as carried by the /oidc/mfa-check URL.
  urlTenant: undefined as string | undefined,
  oidcUiConfig: undefined as unknown,
}));

vi.mock("react-router", () => ({ useNavigate: () => h.navigateMock }));
vi.mock("nuqs", () => ({
  useQueryStates: () => [{ mfa_id: "mfa-1", mfa_type: 2 }],
  parseAsString: { withDefault: () => ({}) },
  parseAsInteger: { withDefault: () => ({}) },
}));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: h.setAuthenticated })),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn() }));
vi.mock("@blocks-idp/mfa/hooks/use-resend-otp", () => ({
  useResendOtp: vi.fn(() => ({ remainingTime: 0, resend: h.resend })),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  useOidcAuthAnimation: vi.fn(() => null),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => h.envTenant }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: () => ({ tenantId: h.urlTenant, themeColor: "#124091" }),
}));

import { MfaCheckFrom } from "./mfa-check-form";
import { OIDC_UI_TEMPLATE_FIXTURE } from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

beforeEach(() => {
  vi.clearAllMocks();
  h.envTenant = "root-project-key";
  h.urlTenant = undefined;
  h.oidcUiConfig = undefined;
});

describe("MfaCheckFrom", () => {
  it("renders the OTP input, resend control and a disabled verify button", () => {
    const { container } = render(<MfaCheckFrom />);
    expect(container.querySelector("input")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /resend code/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /verify/i })).toBeDisabled();
  });

  it("renders tenant-defined MFA actions", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          mfa: { heading: "Confirm identity", submitButton: "Confirm code", resendButton: "Send again" },
        },
      },
    };
    render(<MfaCheckFrom />);
    expect(screen.getByRole("button", { name: /Send again/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Confirm code/ })).toBeDisabled();
  });

  it("hides the optional resend action when its template value is null", () => {
    h.oidcUiConfig = {
      captcha: null,
      template: {
        ...OIDC_UI_TEMPLATE_FIXTURE,
        pages: {
          ...OIDC_UI_TEMPLATE_FIXTURE.pages,
          mfa: { ...OIDC_UI_TEMPLATE_FIXTURE.pages.mfa, resendButton: null },
        },
      },
    };
    render(<MfaCheckFrom />);
    expect(screen.queryByRole("button", { name: /resend/i })).not.toBeInTheDocument();
  });

  it("posts the entered code to the login endpoint and surfaces an invalid-code error", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      headers: { get: () => "application/json" },
      json: async () => ({ error: "invalid_mfa_code" }),
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container } = render(<MfaCheckFrom />);
    const otp = container.querySelector("input") as HTMLInputElement;
    fireEvent.change(otp, { target: { value: "12345" } });

    const verify = screen.getByRole("button", { name: /verify/i });
    await waitFor(() => expect(verify).not.toBeDisabled());
    fireEvent.click(verify);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/oidc/login");
    const body = JSON.parse(options.body as string);
    expect(body.mfa_id).toBe("mfa-1");
    expect(body.mfa_code).toBe("12345");

    expect(
      await screen.findByText("Invalid verification code. Please try again."),
    ).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  const submitCode = async (container: HTMLElement) => {
    const otp = container.querySelector("input") as HTMLInputElement;
    fireEvent.change(otp, { target: { value: "12345" } });
    const verify = screen.getByRole("button", { name: /verify/i });
    await waitFor(() => expect(verify).not.toBeDisabled());
    fireEvent.click(verify);
  };

  it("authenticates and navigates to the profile page on success without a redirect", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({}),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    await waitFor(() => expect(h.setAuthenticated).toHaveBeenCalled());
    expect(h.navigateMock).toHaveBeenCalledWith("/app/profile");
    vi.unstubAllGlobals();
  });

  it("redirects when the response provides a redirect uri", async () => {
    const location = { href: "" };
    Object.defineProperty(window, "location", { value: location, configurable: true });
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({ redirect_uri: "https://redirect.test" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    await waitFor(() => expect(location.href).toBe("https://redirect.test"));
    vi.unstubAllGlobals();
  });

  it("shows the account-locked message", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      headers: { get: () => "application/json" },
      json: async () => ({ error: "account_locked" }),
    });
    vi.stubGlobal("fetch", fetchMock);
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    expect(await screen.findByText(/Your account is locked/)).toBeInTheDocument();
    vi.unstubAllGlobals();
  });

  // Regression: the tenant used to come only from BLOCKS_X_BLOCKS_KEY -- this bundle's
  // own key, always the root IdP -- so every construct's second leg was addressed to the
  // root tenant. CompleteMfaLoginAsync resolves the user against the tenant named by
  // x-blocks-key, so the construct's user id was looked up in the root database and
  // verification failed with `invalid_credentials`.
  it("completes against the tenant named by the url, not the idp's own project key", async () => {
    h.urlTenant = "construct-tenant-key";
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({}),
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [, options] = fetchMock.mock.calls[0];
    expect(JSON.parse(options.body as string).tenant_id).toBe("construct-tenant-key");
    expect(options.headers["X-Blocks-Key"]).toBe("construct-tenant-key");

    vi.unstubAllGlobals();
  });

  it("falls back to the configured project key when the url names no tenant", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({}),
    });
    vi.stubGlobal("fetch", fetchMock);

    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [, options] = fetchMock.mock.calls[0];
    expect(JSON.parse(options.body as string).tenant_id).toBe("root-project-key");
    expect(options.headers["X-Blocks-Key"]).toBe("root-project-key");

    vi.unstubAllGlobals();
  });

  it("triggers the resend handler", () => {
    render(<MfaCheckFrom />);
    fireEvent.click(screen.getByRole("button", { name: /resend code/i }));
    expect(h.resend).toHaveBeenCalled();
  });
});
