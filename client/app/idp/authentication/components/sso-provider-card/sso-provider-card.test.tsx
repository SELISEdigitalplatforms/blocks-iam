import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ISsoProviderConfigurationWithMeta } from "@blocks-idp/authentication/models/sso.model";

const h = vi.hoisted(() => ({ theme: "light", toggleProps: null as Record<string, unknown> | null }));

vi.mock("@/hooks/use-theme", () => ({ useTheme: () => ({ resolvedTheme: h.theme }) }));
vi.mock("@/hooks/use-scoped-path", () => ({ useScopedPath: () => (p: string) => `/scoped/${p}` }));
vi.mock("../sso-provider-status-toggle", () => ({
  SSoProviderStatusToggle: (props: Record<string, unknown>) => {
    h.toggleProps = props;
    return <div data-testid="status-toggle" />;
  },
}));

import { SSOProviderCard, SSOProviderCardSkelton } from "./sso-provider-card";

const config = (overrides: Partial<ISsoProviderConfigurationWithMeta> = {}) =>
  ({
    provider: "google",
    label: "Google",
    description: "Sign in with Google",
    isAvailable: true,
    itemId: "sso1",
    isDisabled: false,
    imageSrc: "/light.png",
    imageSrcDark: "/dark.png",
    ...overrides,
  }) as ISsoProviderConfigurationWithMeta;

const renderCard = (c: ISsoProviderConfigurationWithMeta) =>
  render(
    <MemoryRouter>
      <SSOProviderCard configuration={c} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.theme = "light";
});

describe("SSOProviderCard", () => {
  it("renders the label, description and an active badge for a live provider", () => {
    renderCard(config());
    expect(screen.getByText("Google")).toBeInTheDocument();
    expect(screen.getByText("Sign in with Google")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders a coming-soon badge for an unavailable provider", () => {
    renderCard(config({ isAvailable: false }));
    expect(screen.getByText("Coming soon")).toBeInTheDocument();
  });

  it("uses the dark image source in dark mode", () => {
    h.theme = "dark";
    renderCard(config());
    expect(screen.getByAltText("socical_icon")).toHaveAttribute("src", "/dark.png");
  });

  it("opens the configure menu with a scoped link", () => {
    renderCard(config());
    fireEvent.pointerDown(
      screen.getByRole("button"),
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );
    const link = screen.getByRole("link", { name: "Configure" });
    expect(link.getAttribute("href")).toContain("/scoped/");
    expect(screen.getByText("Disable")).toBeInTheDocument();
  });

  it("renders the skeleton placeholder", () => {
    const { container } = render(<SSOProviderCardSkelton />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });
});
