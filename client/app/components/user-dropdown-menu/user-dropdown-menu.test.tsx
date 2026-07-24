import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  me: { data: { firstName: "Ada", lastName: "Lovelace", email: "ada@x.com", roles: {} } } as unknown,
  orgs: {} as Record<string, unknown>,
  logout: vi.fn(),
  isPending: false,
}));

vi.mock("@/idp/iam/hooks/use-user", () => ({ useGetMe: () => ({ data: h.me }) }));
vi.mock("@/idp/iam/hooks/use-organization", () => ({
  useGetMyOrganizations: () => h.orgs,
}));
vi.mock("@/idp/authentication/hooks/use-auth", () => ({
  useLogout: () => ({ isPending: h.isPending, mutateAsync: h.logout }),
}));
vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return {
    ...actual,
    useAuthStore: () => ({ setUnAuthenticated: vi.fn(), clearTokens: vi.fn() }),
    useProjectStore: () => ({ resetProjectStore: vi.fn() }),
  };
});
vi.mock("@/cross-modules/localization/store/use-language-view-store", () => ({
  useLanguageViewStore: () => ({ resetSelectedLanguages: vi.fn() }),
}));
vi.mock("@/providers/query-provider", () => ({ getQueryClient: () => ({ clear: vi.fn() }) }));

import { UserDropdownMenu } from "./user-dropdown-menu";

beforeEach(() => {
  vi.clearAllMocks();
  h.me = { data: { firstName: "Ada", lastName: "Lovelace", email: "ada@x.com", roles: {} } };
  h.orgs = { data: { organizations: [] }, isLoading: false };
});

describe("UserDropdownMenu", () => {
  it("renders the user menu trigger with the avatar initials", () => {
    render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    expect(screen.getByLabelText("Open user menu")).toBeInTheDocument();
    expect(screen.getByText("AL")).toBeInTheDocument();
  });

  it("renders a fallback avatar icon when the user has no name", () => {
    h.me = { data: { firstName: "", lastName: "", email: "", roles: {} } };
    const { container } = render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    expect(container.querySelector("svg")).not.toBeNull();
  });
});
