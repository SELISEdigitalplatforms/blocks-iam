import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  signinByOidcEmail: vi.fn(),
  resolvedTheme: "light",
  showError: vi.fn(),
  hrefSpy: vi.fn(),
  markerAtRedirect: undefined as string | null | undefined,
}));

vi.mock("@/hooks/use-toast", () => ({ showErrorToast: (a: unknown) => h.showError(a) }));
vi.mock("@/hooks/use-theme", () => ({ useTheme: () => ({ resolvedTheme: h.resolvedTheme }) }));
vi.mock("@/lib/runtime-env", () => ({ getRuntimeEnv: () => "https://iam.test" }));
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
  OIDC_SSO_RETURN_URI_STORAGE_KEY: "oidc-sso-return-uri",
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

  it("sends the user to IAM's sign-in when there is no OIDC request to attach to", async () => {
    render(<SSOSigninCard providerConfig={provider} />);
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.hrefSpy).toHaveBeenCalledWith("/login"));
    expect(h.signinByOidcEmail).not.toHaveBeenCalled();
  });

  it("keeps the application redirect_uri for the error page before leaving", async () => {
    h.signinByOidcEmail.mockResolvedValue({ authorizationUrl: "https://oidc.google/authorize" });
    render(
      <SSOSigninCard
        providerConfig={provider}
        mode="oidc"
        oidcContext={{ clientId: "c", redirectUri: "https://app.example.com/cb", tenantId: "t1" }}
      />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.hrefSpy).toHaveBeenCalled());
    expect(sessionStorage.getItem("oidc-sso-return-uri")).toBe("https://app.example.com/cb");
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

  it("shows an error toast when the oidc endpoint returns an error", async () => {
    h.signinByOidcEmail.mockResolvedValue({ error: "provider down" });
    render(
      <SSOSigninCard providerConfig={provider} mode="oidc" oidcContext={{ clientId: "c", tenantId: "t1" }} />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "provider down" }));
  });

  it("explains an unregistered provider redirect URI", async () => {
    h.signinByOidcEmail.mockResolvedValue({ error: "invalid_redirect_uri" });
    render(
      <SSOSigninCard providerConfig={provider} mode="oidc" oidcContext={{ clientId: "c", tenantId: "t1" }} />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({
        errors: "This sign-in option is not set up for this page. Contact your administrator.",
      }),
    );
    expect(h.hrefSpy).not.toHaveBeenCalled();
  });

  it("explains an inactive application when the request is refused", async () => {
    h.signinByOidcEmail.mockRejectedValue({ errors: { error: "invalid_client" } });
    render(
      <SSOSigninCard providerConfig={provider} mode="oidc" oidcContext={{ clientId: "c", tenantId: "t1" }} />,
    );
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({
        errors: "The application that sent you here is not recognised.",
      }),
    );
  });
});
