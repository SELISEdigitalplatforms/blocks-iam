import { act, render, screen, fireEvent, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { ReactNode } from "react";

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
  animCtx: null as Record<string, ReturnType<typeof vi.fn>> | null,
  returnUrl: "",
  mfaId: "mfa-1",
  pathname: "/oidc/mfa-check",
  // What resolveLoginReturnTarget answers: the flow's own /oidc/login, or the application.
  returnTarget: { href: "/oidc/login?clientId=os-client&tenant_id=t1", external: false },
  // The onExpired the form hands the resend hook.
  onResendExpired: undefined as (() => void) | undefined,
}));

vi.mock("react-router", () => ({
  useNavigate: () => h.navigateMock,
  useLocation: () => ({ pathname: h.pathname }),
  Link: ({ to, children, ...rest }: { to: string; children: ReactNode }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
}));
vi.mock("nuqs", () => ({
  useQueryStates: () => [{ mfa_id: h.mfaId, mfa_type: 2, returnUrl: h.returnUrl }],
  parseAsString: { withDefault: () => ({}) },
  parseAsInteger: { withDefault: () => ({}) },
}));
vi.mock("@/store/useAuthStore", () => ({
  useAuthStore: vi.fn(() => ({ setAuthenticated: h.setAuthenticated })),
}));
vi.mock("@/hooks/use-toast", () => ({ showErrorToast: vi.fn() }));
vi.mock("@blocks-idp/mfa/hooks/use-resend-otp", () => ({
  useResendOtp: vi.fn(({ onExpired }: { onExpired?: () => void }) => {
    h.onResendExpired = onExpired;
    return { remainingTime: 0, resend: h.resend };
  }),
}));
vi.mock("@blocks-idp/authentication/pages/oidc/oidc-auth-shell", () => ({
  useOidcAuthAnimation: vi.fn(() => h.animCtx),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ data: h.oidcUiConfig }),
}));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => h.envTenant }));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  extractOIDCParams: () => ({ tenantId: h.urlTenant, themeColor: "#124091" }),
  resolveLoginReturnTarget: vi.fn(() => h.returnTarget),
}));

import { MfaCheckFrom } from "./mfa-check-form";

const SESSION_EXPIRED_MESSAGE =
  "Your sign-in took too long, so this code can no longer be used. Please sign in again.";
import { resolveLoginReturnTarget } from "@blocks-idp/authentication/utils/oidc-utils";
import { useResendOtp } from "@blocks-idp/mfa/hooks/use-resend-otp";
import {
  DEFAULT_OIDC_UI_TEMPLATE_FIXTURE,
  OIDC_UI_TEMPLATE_FIXTURE,
} from "@blocks-idp/authentication/test-utils/oidc-ui-template-fixture";

beforeEach(() => {
  vi.clearAllMocks();
  h.envTenant = "root-project-key";
  h.urlTenant = undefined;
  h.animCtx = null;
  h.returnUrl = "";
  h.mfaId = "mfa-1";
  h.pathname = "/oidc/mfa-check";
  h.returnTarget = { href: "/oidc/login?clientId=os-client&tenant_id=t1", external: false };
  h.onResendExpired = undefined;
  h.oidcUiConfig = { captcha: null, template: DEFAULT_OIDC_UI_TEMPLATE_FIXTURE };
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
    fireEvent.change(otp, { target: { value: "123456" } });

    const verify = screen.getByRole("button", { name: /verify/i });
    await waitFor(() => expect(verify).not.toBeDisabled());
    fireEvent.click(verify);

    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    const [url, options] = fetchMock.mock.calls[0];
    expect(url).toBe("/api/oidc/login");
    const body = JSON.parse(options.body as string);
    expect(body.mfa_id).toBe("mfa-1");
    expect(body.mfa_code).toBe("123456");

    expect(
      await screen.findByText("Invalid verification code. Please try again."),
    ).toBeInTheDocument();

    vi.unstubAllGlobals();
  });

  const submitCode = async (container: HTMLElement) => {
    const otp = container.querySelector("input") as HTMLInputElement;
    fireEvent.change(otp, { target: { value: "123456" } });
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

  it("returns a device-flow login to its same-origin returnUrl", async () => {
    h.returnUrl = "/device/t1?user_code=ABCD-1234";
    const location = { href: "", origin: "https://iam.example.com" };
    Object.defineProperty(globalThis, "location", { value: location, configurable: true });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({ success: true }),
    }));
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    await waitFor(() => expect(location.href).toBe("https://iam.example.com/device/t1?user_code=ABCD-1234"));
    expect(h.navigateMock).not.toHaveBeenCalled();
    vi.unstubAllGlobals();
  });

  // Bug: an http returnUrl (TLS proxy) was refused on the https page and the user landed on the console.
  it("shows an error instead of the console when the device-flow returnUrl is refused", async () => {
    h.returnUrl = "http://iam.example.com/device/t1?user_code=ABCD-1234";
    const location = { href: "", origin: "https://iam.example.com" };
    Object.defineProperty(globalThis, "location", { value: location, configurable: true });
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue({
      ok: true,
      headers: { get: () => "application/json" },
      json: async () => ({ success: true }),
    }));
    const { container } = render(<MfaCheckFrom />);
    await submitCode(container);
    expect(await screen.findByText(/return address is not valid/)).toBeInTheDocument();
    expect(h.navigateMock).not.toHaveBeenCalled();
    expect(location.href).toBe("");
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

  describe("redirect target after verification", () => {
    const APP_URL = "com.klaxai.app://login/callback?code=abc&state=xyz";
    const NO_TARGET = "Login succeeded but no redirect target was provided";
    const originalLocation = window.location;
    let assignments: string[];

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

    afterEach(() => {
      Object.defineProperty(window, "location", { configurable: true, value: originalLocation });
      vi.unstubAllGlobals();
    });

    const respondWith = (body: unknown) =>
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue({
          ok: true,
          headers: { get: () => "application/json" },
          json: async () => body,
        }),
      );

    it("opens the app once and shows the hand-off in place of the form (H8)", async () => {
      respondWith({ redirect_uri: APP_URL });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);

      const link = await screen.findByRole("link", { name: "Return to the app" });
      expect(link).toHaveAttribute("href", APP_URL);
      await waitFor(() => expect(link).toHaveFocus());
      expect(screen.getByRole("heading", { name: "Signed in" })).toBeInTheDocument();
      expect(container.querySelector("form")).toBeNull();
      expect(assignments).toEqual([APP_URL]);
      expect(h.setAuthenticated).toHaveBeenCalled();
    });

    it("skips the success animation for an app-scheme target when a shell is present", async () => {
      h.animCtx = {
        startAnimation: vi.fn(),
        succeedAnimation: vi.fn().mockResolvedValue(undefined),
        failAnimation: vi.fn().mockResolvedValue(undefined),
        resetAnimation: vi.fn(),
      };
      respondWith({ redirect_uri: APP_URL });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);

      await screen.findByRole("link", { name: "Return to the app" });
      expect(h.animCtx.succeedAnimation).not.toHaveBeenCalled();
      expect(h.animCtx.resetAnimation).toHaveBeenCalled();
      expect(assignments).toEqual([APP_URL]);
    });

    it.each(["javascript:alert(1)", "myapp://callback"])(
      "shows the no-redirect-target error in the form for %s instead of idling (C10)",
      async (redirectUri) => {
        h.animCtx = {
          startAnimation: vi.fn(),
          succeedAnimation: vi.fn().mockResolvedValue(undefined),
          failAnimation: vi.fn().mockResolvedValue(undefined),
          resetAnimation: vi.fn(),
        };
        respondWith({ redirect_uri: redirectUri });
        const { container } = render(<MfaCheckFrom />);
        await submitCode(container);

        expect(await screen.findByText(NO_TARGET)).toBeInTheDocument();
        expect(h.animCtx.failAnimation).toHaveBeenCalledWith(NO_TARGET);
        expect(assignments).toEqual([]);
        expect(container.querySelector("form")).not.toBeNull();
      },
    );

    it("follows a web redirect_uri once, unchanged", async () => {
      respondWith({ redirect_uri: "https://app.example.com/cb?code=1" });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);

      await waitFor(() => expect(assignments).toEqual(["https://app.example.com/cb?code=1"]));
      expect(screen.queryByRole("link", { name: "Return to the app" })).not.toBeInTheDocument();
    });
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

  describe("disabled organization", () => {
    const ORGANIZATION_DISABLED_MESSAGE =
      "Your organization is disabled. Please contact your administrator or support.";

    const respondWith = (ok: boolean, body: unknown) =>
      vi.stubGlobal("fetch", vi.fn().mockResolvedValue({
        ok,
        headers: { get: () => "application/json" },
        json: async () => body,
      }));

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it("shows the message in the alert slot with the fail animation for organization_disabled", async () => {
      h.animCtx = { startAnimation: vi.fn(), failAnimation: vi.fn(), succeedAnimation: vi.fn(), resetAnimation: vi.fn() };
      respondWith(false, { error: "organization_disabled", error_description: "Server text" });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);
      expect(await screen.findByRole("alert")).toHaveTextContent(ORGANIZATION_DISABLED_MESSAGE);
      expect(screen.queryByText("Server text")).not.toBeInTheDocument();
      expect(h.animCtx.failAnimation).toHaveBeenCalledWith(ORGANIZATION_DISABLED_MESSAGE);
      expect(h.navigateMock).not.toHaveBeenCalled();
    });

    it("stays on the form when a portal sign-in succeeds with org_status disabled", async () => {
      h.pathname = "/mfa-check";
      respondWith(true, { org_status: "disabled" });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);
      expect(await screen.findByRole("alert")).toHaveTextContent(ORGANIZATION_DISABLED_MESSAGE);
      expect(h.navigateMock).not.toHaveBeenCalled();
      expect(h.setAuthenticated).not.toHaveBeenCalled();
    });

    it("still follows the redirect of an OIDC login that succeeds with org_status disabled", async () => {
      const location = { href: "" };
      Object.defineProperty(window, "location", { value: location, configurable: true });
      respondWith(true, { redirect_uri: "https://redirect.test", org_status: "disabled" });
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);
      await waitFor(() => expect(location.href).toBe("https://redirect.test"));
      expect(screen.queryByText(ORGANIZATION_DISABLED_MESSAGE)).not.toBeInTheDocument();
    });
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

  // Regression (dev 2026-11-02): a construct app's hosted MFA page resent with the IdP's own key,
  // so the server looked the construct user up in the root database: 400 `user_not_found`.
  it("resends on the tenant named by the url, like verify", () => {
    h.urlTenant = "construct-tenant-key";
    render(<MfaCheckFrom />);
    expect(vi.mocked(useResendOtp)).toHaveBeenLastCalledWith(
      expect.objectContaining({ mfaId: "mfa-1", tenantId: "construct-tenant-key" }),
    );
  });

  it("resends on the configured project key when the url names no tenant", () => {
    render(<MfaCheckFrom />);
    expect(vi.mocked(useResendOtp)).toHaveBeenLastCalledWith(
      expect.objectContaining({ mfaId: "mfa-1", tenantId: "root-project-key" }),
    );
  });

  describe("way back to sign in", () => {
    const expiredResponse = () =>
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue({
          ok: false,
          status: 400,
          headers: { get: () => "application/json" },
          json: async () => ({
            error: "invalid_mfa_session",
            error_description: "Mfa login session is expired or invalid",
          }),
        }),
      );

    afterEach(() => vi.unstubAllGlobals());

    it("always offers the sign-in of the same flow next to the code form", () => {
      const { container } = render(<MfaCheckFrom />);
      const link = screen.getByRole("link", { name: "Back to Sign In" });
      expect(link).toHaveAttribute("href", "/oidc/login?clientId=os-client&tenant_id=t1");
      expect(container.querySelector("input")).toBeInTheDocument();
      expect(resolveLoginReturnTarget).toHaveBeenCalledWith(true, { preferOidcLogin: true });
    });

    it("names the application when the way back leads there", () => {
      h.pathname = "/mfa-check";
      h.returnTarget = { href: "https://construct.example.com", external: true };
      render(<MfaCheckFrom />);
      const link = screen.getByRole("link", { name: "Back to application" });
      expect(link).toHaveAttribute("href", "https://construct.example.com");
      expect(resolveLoginReturnTarget).toHaveBeenCalledWith(false, { preferOidcLogin: true });
    });

    it("replaces the form with the expired notice when the sign-in is gone (authenticator or email)", async () => {
      const onSessionExpired = vi.fn();
      expiredResponse();
      const { container } = render(<MfaCheckFrom onSessionExpired={onSessionExpired} />);
      await submitCode(container);

      expect(await screen.findByText(SESSION_EXPIRED_MESSAGE)).toBeInTheDocument();
      expect(screen.queryByText("Mfa login session is expired or invalid")).not.toBeInTheDocument();
      expect(container.querySelector("form")).toBeNull();
      expect(screen.queryByRole("button", { name: /verify/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /resend code/i })).not.toBeInTheDocument();
      expect(screen.getByRole("link", { name: /Back to Sign In/ })).toHaveAttribute(
        "href",
        "/oidc/login?clientId=os-client&tenant_id=t1",
      );
      expect(onSessionExpired).toHaveBeenCalledTimes(1);
    });

    it("plays the panel's failure state with the expired message inside the shell", async () => {
      h.animCtx = {
        startAnimation: vi.fn(),
        succeedAnimation: vi.fn().mockResolvedValue(undefined),
        failAnimation: vi.fn().mockResolvedValue(undefined),
        resetAnimation: vi.fn(),
      };
      expiredResponse();
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);
      await screen.findByText(SESSION_EXPIRED_MESSAGE);
      expect(h.animCtx.failAnimation).toHaveBeenCalledWith(SESSION_EXPIRED_MESSAGE);
    });

    it("shows the expired notice when a resend finds the email challenge gone", async () => {
      const onSessionExpired = vi.fn();
      const { container } = render(<MfaCheckFrom onSessionExpired={onSessionExpired} />);
      expect(h.onResendExpired).toBeTypeOf("function");

      act(() => h.onResendExpired?.());

      expect(await screen.findByText(SESSION_EXPIRED_MESSAGE)).toBeInTheDocument();
      expect(container.querySelector("form")).toBeNull();
      expect(onSessionExpired).toHaveBeenCalledTimes(1);
    });

    it("shows the expired notice straight away for a link without a challenge", () => {
      h.mfaId = "";
      const { container } = render(<MfaCheckFrom />);
      expect(screen.getByText(SESSION_EXPIRED_MESSAGE)).toBeInTheDocument();
      expect(container.querySelector("form")).toBeNull();
      expect(screen.getByRole("link", { name: /Back to Sign In/ })).toBeInTheDocument();
    });

    it("keeps the way back after an account-locked answer", async () => {
      vi.stubGlobal(
        "fetch",
        vi.fn().mockResolvedValue({
          ok: false,
          headers: { get: () => "application/json" },
          json: async () => ({ error: "account_locked" }),
        }),
      );
      const { container } = render(<MfaCheckFrom />);
      await submitCode(container);
      await screen.findByText(/Your account is locked/);
      expect(screen.getByRole("link", { name: "Back to Sign In" })).toBeInTheDocument();
    });
  });
});
