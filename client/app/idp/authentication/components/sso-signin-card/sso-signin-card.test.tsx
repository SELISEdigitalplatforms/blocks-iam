import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  getSocialLoginEndpoint: vi.fn(),
  signinByOidcEmail: vi.fn(),
  resolvedTheme: "light",
  showError: vi.fn(),
  hrefSpy: vi.fn(),
  markerAtRedirect: undefined as string | null | undefined,
}));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => h.showError(a) }));
vi.mock("@/hooks/use-theme", () => ({ useTheme: () => ({ resolvedTheme: h.resolvedTheme }) }));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "https://iam.test" }));
vi.mock("@blocks-idp/authentication/services/oauth.service", () => ({
  oauthService: { getSocialLoginEndpoint: (...a: unknown[]) => h.getSocialLoginEndpoint(...a) },
}));
vi.mock("@blocks-idp/authentication/services/auth.service", () => ({
  authService: { signinByOidcEmail: (...a: unknown[]) => h.signinByOidcEmail(...a) },
}));
vi.mock("@blocks-idp/authentication/utils/sanitize-provider-url.util", () => ({
  sanitizeProviderUrl: (u: string) => u,
}));
vi.mock("@blocks-idp/authentication/utils/oidc-utils", () => ({
  buildOIDCNavigationUrl: (p: string) => p,
  OIDC_DEVICE_RETURN_URL_STORAGE_KEY: "oidc-device-return-url",
  OIDC_APP_HANDOFF_STORAGE_KEY: "oidc_app_handoff",
}));

import { SSOSigninCard } from "./sso-signin-card";

const provider = {
  provider: "google",
  label: "Google",
  clientId: "cid",
  audience: "aud",
  imageSrc: "/g-light.svg",
  imageSrcDark: "/g-dark.svg",
} as unknown as Parameters<typeof SSOSigninCard>[0]["providerConfig"];

beforeEach(() => {
  vi.clearAllMocks();
  h.hrefSpy.mockReset();
  sessionStorage.clear();
  h.resolvedTheme = "light";
  Object.defineProperty(window, "location", {
    value: {
      get href() {
        return "";
      },
      set href(v: string) {
        h.hrefSpy(v);
      },
    },
    configurable: true,
  });
});

describe("SSOSigninCard", () => {
  it("renders the provider label when requested", () => {
    render(<SSOSigninCard providerConfig={provider} withLabel labelMode="full" />);
    expect(screen.getByText("Sign in with Google")).toBeInTheDocument();
  });

  it("redirects to the social login endpoint in the default flow", async () => {
    h.getSocialLoginEndpoint.mockResolvedValue({ providerUrl: "https://accounts.google.com/o" });
    render(<SSOSigninCard providerConfig={provider} />);
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.getSocialLoginEndpoint).toHaveBeenCalled());
    await waitFor(() => expect(h.hrefSpy).toHaveBeenCalledWith("https://accounts.google.com/o"));
  });

  it("uses the oidc flow when mode is oidc", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://oidc.google/authorize" });
    render(
      <SSOSigninCard
        providerConfig={provider}
        mode="oidc"
        oidcContext={{ clientId: "c", redirectUri: "r", tenantId: "t1" }}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.signinByOidcEmail).toHaveBeenCalled());
    await waitFor(() => expect(h.hrefSpy).toHaveBeenCalledWith("https://oidc.google/authorize"));
  });

  describe("native-app hand-off marker", () => {
    const startOidc = async (redirectUri: string | undefined) => {
      h.signinByOidcEmail.mockImplementation(async () => {
        // Asserted at the moment of leaving, not just eventually.
        h.hrefSpy.mockImplementation(() => {
          h.markerAtRedirect = sessionStorage.getItem("oidc_app_handoff");
        });
        return { authorizationUrl: "https://login.microsoftonline.com/authorize" };
      });
      render(
        <SSOSigninCard
          providerConfig={provider}
          mode="oidc"
          oidcContext={{ clientId: "c", redirectUri, tenantId: "t1" }}
        />,
      );
      fireEvent.click(screen.getByRole("button"));
      await waitFor(() =>
        expect(h.hrefSpy).toHaveBeenCalledWith("https://login.microsoftonline.com/authorize"),
      );
    };

    beforeEach(() => {
      h.markerAtRedirect = undefined;
    });

    it("is set to 1 before leaving for the provider when redirect_uri is an app scheme (H9)", async () => {
      await startOidc("com.klaxai.app://login/callback");
      expect(h.markerAtRedirect).toBe("1");
      expect(sessionStorage.getItem("oidc_app_handoff")).toBe("1");
    });

    it("is removed for a web redirect_uri, clearing a stale marker (H9, C13)", async () => {
      sessionStorage.setItem("oidc_app_handoff", "1");
      await startOidc("https://app.example.com/cb");
      expect(h.markerAtRedirect).toBeNull();
      expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
    });

    it("is removed when there is no redirect_uri (device flow)", async () => {
      sessionStorage.setItem("oidc_app_handoff", "1");
      await startOidc(undefined);
      expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
    });

    it("is not touched by the standard embed flow", async () => {
      sessionStorage.setItem("oidc_app_handoff", "1");
      h.getSocialLoginEndpoint.mockResolvedValue({ providerUrl: "https://accounts.google.com/o" });
      render(<SSOSigninCard providerConfig={provider} />);
      fireEvent.click(screen.getByRole("button"));
      await waitFor(() => expect(h.hrefSpy).toHaveBeenCalled());
      expect(sessionStorage.getItem("oidc_app_handoff")).toBe("1");
    });
  });

  it("stashes the device-flow returnUrl before leaving for the provider", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://oidc.google/authorize" });
    render(
      <SSOSigninCard
        providerConfig={provider}
        mode="oidc"
        oidcContext={{ clientId: "c", tenantId: "t1", returnUrl: "/oidc/device/entry" }}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.hrefSpy).toHaveBeenCalled());
    expect(sessionStorage.getItem("oidc-device-return-url")).toBe("/oidc/device/entry");
  });

  it("does not leave or set the marker when the oidc flow returns no authorization URL", async () => {
    h.signinByOidcEmail.mockResolvedValue({});
    render(
      <SSOSigninCard
        providerConfig={provider}
        mode="oidc"
        oidcContext={{ clientId: "c", redirectUri: "com.klaxai.app://cb", tenantId: "t1" }}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "No authorization URL provided." }),
    );
    expect(h.hrefSpy).not.toHaveBeenCalled();
    expect(sessionStorage.getItem("oidc_app_handoff")).toBeNull();
  });

  it("shows an error toast when the endpoint returns an error", async () => {
    h.getSocialLoginEndpoint.mockResolvedValue({ error: "provider down" });
    render(<SSOSigninCard providerConfig={provider} />);
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "provider down" }));
  });
});
