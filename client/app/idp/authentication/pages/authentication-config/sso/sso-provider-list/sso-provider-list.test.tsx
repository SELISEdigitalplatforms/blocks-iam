import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  credentials: [] as { provider: string }[],
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => ({ isLoading: h.isLoading }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-sso", () => ({
  useGetSsoCredentials: () => ({ data: h.credentials }),
}));
vi.mock("@blocks-idp/authentication/constants/sso-providers.constant", () => ({
  SSO_PROVIDERS: { google: "google", github: "github" },
  SOCIAL_AUTH_PROVIDERS_CONFIG: {
    google: { provider: "google", label: "Google" },
    github: { provider: "github", label: "GitHub" },
  },
}));
vi.mock("@blocks-idp/authentication/components/sso-provider-card/sso-provider-card", () => ({
  SSOProviderCard: ({ configuration }: { configuration: { provider: string } }) => (
    <div data-testid="provider-card">{configuration.provider}</div>
  ),
  SSOProviderCardSkelton: () => <div data-testid="provider-skeleton" />,
}));

import { SSOProviderList } from "./sso-provider-list";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.credentials = [];
});

describe("SSOProviderList", () => {
  it("renders skeletons while the auth config loads", () => {
    h.isLoading = true;
    render(<SSOProviderList />);
    expect(screen.getAllByTestId("provider-skeleton").length).toBeGreaterThan(0);
  });

  it("renders a card for every known provider", () => {
    render(<SSOProviderList />);
    const cards = screen.getAllByTestId("provider-card");
    expect(cards).toHaveLength(2);
    expect(cards.map((c) => c.textContent)).toEqual(["google", "github"]);
  });

  it("merges configured credentials into the provider base config", () => {
    h.credentials = [{ provider: "google" }];
    render(<SSOProviderList />);
    expect(screen.getAllByTestId("provider-card")).toHaveLength(2);
  });
});
