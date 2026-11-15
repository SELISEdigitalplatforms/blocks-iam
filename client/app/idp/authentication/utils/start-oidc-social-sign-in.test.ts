import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  signinByOidcEmail: vi.fn(),
  showError: vi.fn(),
  hrefSpy: vi.fn(),
  replaceSpy: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => h.showError(a) }));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "https://iam.test" }));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { signinByOidcEmail: (...a: unknown[]) => h.signinByOidcEmail(...a) },
}));

import { startOidcSocialSignIn } from "./start-oidc-social-sign-in";

const context = {
  clientId: "client-1",
  redirectUri: "https://app.example.com/cb",
  state: "st-1",
  nonce: "n-1",
  scope: "openid",
  code_challenge: "cc",
  code_challenge_method: "S256",
  tenantId: "tenant-1",
};

beforeEach(() => {
  vi.clearAllMocks();
  sessionStorage.clear();
  Object.defineProperty(window, "location", {
    configurable: true,
    value: {
      get href() {
        return "";
      },
      set href(v: string) {
        h.hrefSpy(v);
      },
      replace: (v: string) => h.replaceSpy(v),
    },
  });
});

describe("startOidcSocialSignIn", () => {
  it("asks IAM for the provider's URL for this OIDC request and its own callback", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://login.microsoftonline.com/authorize" });
    await startOidcSocialSignIn({ provider: "microsoft", providerClientId: "ms-cid", oidcContext: context });
    expect(h.signinByOidcEmail).toHaveBeenCalledWith({
      provider: "microsoft",
      clientId: "client-1",
      redirectUri: "https://app.example.com/cb",
      scope: "openid",
      state: "st-1",
      nonce: "n-1",
      code_challenge: "cc",
      code_challenge_method: "S256",
      tenantId: "tenant-1",
      provider_client_id: "ms-cid",
      provider_redirect_uri: "https://iam.test/oidc/microsoft/callback/tenant-1",
    });
  });

  it("navigates like a link by default (the button's behaviour)", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://accounts.google.com/o/oauth2/auth" });
    const leaving = await startOidcSocialSignIn({ provider: "google", providerClientId: "g", oidcContext: context });
    expect(leaving).toBe(true);
    expect(h.hrefSpy).toHaveBeenCalledWith("https://accounts.google.com/o/oauth2/auth");
    expect(h.replaceSpy).not.toHaveBeenCalled();
    expect(sessionStorage.getItem("oidc-sso-return-uri")).toBe("https://app.example.com/cb");
  });

  it("replaces the page in history when asked, so Back skips it", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://accounts.google.com/o/oauth2/auth" });
    const leaving = await startOidcSocialSignIn({
      provider: "google",
      providerClientId: "g",
      oidcContext: context,
      replace: true,
    });
    expect(leaving).toBe(true);
    expect(h.replaceSpy).toHaveBeenCalledWith("https://accounts.google.com/o/oauth2/auth");
    expect(h.hrefSpy).not.toHaveBeenCalled();
  });

  it("stays and explains when IAM refuses to start", async () => {
    h.signinByOidcEmail.mockResolvedValue({ error: "invalid_redirect_uri" });
    const leaving = await startOidcSocialSignIn({ provider: "google", providerClientId: "g", oidcContext: context, replace: true });
    expect(leaving).toBe(false);
    expect(h.showError).toHaveBeenCalledWith({
      errors: "This sign-in option is not set up for this page. Contact your administrator.",
    });
    expect(h.replaceSpy).not.toHaveBeenCalled();
  });

  it("stays when IAM returns no provider URL", async () => {
    h.signinByOidcEmail.mockResolvedValue({});
    const leaving = await startOidcSocialSignIn({ provider: "google", providerClientId: "g", oidcContext: context });
    expect(leaving).toBe(false);
    expect(h.showError).toHaveBeenCalledWith({ errors: "No authorization URL provided." });
  });

  it("stays when the request throws", async () => {
    vi.spyOn(console, "error").mockImplementation(() => {});
    h.signinByOidcEmail.mockRejectedValue(new Error("network"));
    const leaving = await startOidcSocialSignIn({ provider: "google", providerClientId: "g", oidcContext: context });
    expect(leaving).toBe(false);
    expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" });
  });
});
