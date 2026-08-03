import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  getSocialLoginEndpoint: vi.fn(),
  signinByOidcEmail: vi.fn(),
  resolvedTheme: "light",
  showError: vi.fn(),
  hrefSpy: vi.fn(),
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

  it("shows an error toast when the endpoint returns an error", async () => {
    h.getSocialLoginEndpoint.mockResolvedValue({ error: "provider down" });
    render(<SSOSigninCard providerConfig={provider} />);
    fireEvent.click(screen.getByRole("button"));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "provider down" }));
  });
});
