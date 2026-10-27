import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { LoginOption } from "@blocks-idp/authentication/models/auth.model";

const h = vi.hoisted(() => ({ isPending: false }));

vi.mock("@blocks-idp/authentication/hooks/use-sso-activation", () => ({
  useSsoActivation: () => ({ isPending: h.isPending }),
}));
vi.mock("@blocks-idp/authentication/constants/sso-providers.constant", () => ({
  SOCIAL_AUTH_PROVIDERS_CONFIG: {
    google: { provider: "google", isAvailable: true },
    github: { provider: "github", isAvailable: true },
  },
}));
vi.mock("@blocks-idp/authentication/components/sso-signin-card", () => ({
  SSOSigninCard: ({
    providerConfig,
    mode,
    oidcContext,
  }: {
    providerConfig: { provider: string };
    mode?: string;
    oidcContext?: { clientId?: string; state?: string };
  }) => (
    <div
      data-testid="sso-card"
      data-mode={mode}
      data-client={oidcContext?.clientId ?? ""}
      data-state={oidcContext?.state ?? ""}
    >
      {providerConfig.provider}
    </div>
  ),
}));
vi.mock("@/components/loader-spinner/loader-spinner", () => ({
  default: () => <div data-testid="spinner" />,
}));

import { SsoSignin } from "./sso-signin";

const loginOption = {
  ssoInfo: [{ provider: "google", clientId: "g-client", redirectUris: ["https://cb"] }],
} as unknown as LoginOption;

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  window.history.replaceState(null, "", "/");
});

describe("SsoSignin", () => {
  it("renders a card only for configured, available providers", () => {
    render(<SsoSignin loginOption={loginOption} />);
    const cards = screen.getAllByTestId("sso-card");
    expect(cards).toHaveLength(1);
    expect(cards[0]).toHaveTextContent("google");
  });

  it("renders nothing and no spinner when there are no providers", () => {
    render(<SsoSignin loginOption={{ ssoInfo: [] } as unknown as LoginOption} />);
    expect(screen.queryByTestId("sso-card")).not.toBeInTheDocument();
    expect(screen.queryByTestId("spinner")).not.toBeInTheDocument();
  });

  it("shows the loading spinner while an SSO activation is pending", () => {
    h.isPending = true;
    render(<SsoSignin loginOption={loginOption} />);
    expect(screen.getByTestId("spinner")).toBeInTheDocument();
  });

  it("reads the OIDC request from the URL when the page passes none (signup)", () => {
    window.history.replaceState(
      null,
      "",
      "/oidc/signup/t1?clientId=app-1&redirect_uri=https%3A%2F%2Fapp.example.com%2Fcb&state=s1",
    );
    render(<SsoSignin loginOption={loginOption} />);
    const card = screen.getByTestId("sso-card");
    expect(card).toHaveAttribute("data-mode", "oidc");
    expect(card).toHaveAttribute("data-client", "app-1");
    expect(card).toHaveAttribute("data-state", "s1");
  });

  it("keeps the default mode when neither the page nor the URL has a client", () => {
    render(<SsoSignin loginOption={loginOption} />);
    expect(screen.getByTestId("sso-card")).toHaveAttribute("data-mode", "default");
  });
});
