import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

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
vi.mock("@seliseblocks/genesis-os", async (importActual) => {
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

  const openMenu = () =>
    fireEvent.pointerDown(
      screen.getByLabelText("Open user menu"),
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );

  it("shows the profile header, roles and profile link when opened", async () => {
    h.me = {
      data: {
        firstName: "Ada",
        lastName: "Lovelace",
        email: "ada@x.com",
        roles: { o1: ["Admin"] },
        lastUsedOrganizationId: "o1",
      },
    };
    render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    openMenu();
    expect(await screen.findByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@x.com")).toBeInTheDocument();
    expect(screen.getByText(/Admin/)).toBeInTheDocument();
    expect(screen.getByText("My Profile").closest("a")).toHaveAttribute("href", "/profile");
  });

  it("lists organizations and marks the active one", async () => {
    h.me = { data: { firstName: "Ada", roles: {}, lastUsedOrganizationId: "o1" } };
    h.orgs = {
      data: { organizations: [{ itemId: "o1", name: "Acme" }, { itemId: "o2", name: "Globex" }] },
      isLoading: false,
    };
    render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    openMenu();
    expect(await screen.findByText("Acme")).toBeInTheDocument();
    expect(screen.getByText("Globex")).toBeInTheDocument();
  });

  it("shows the empty organizations state", async () => {
    h.orgs = { data: { organizations: [] }, isLoading: false };
    render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    openMenu();
    expect(await screen.findByText("No organizations found.")).toBeInTheDocument();
  });

  it("logs out and redirects to login", async () => {
    h.logout.mockResolvedValue(undefined);
    const replace = vi.fn();
    Object.defineProperty(window, "location", {
      value: { origin: "http://localhost", replace },
      configurable: true,
    });
    render(
      <MemoryRouter>
        <UserDropdownMenu />
      </MemoryRouter>,
    );
    openMenu();
    fireEvent.click(await screen.findByText("Log out"));
    await waitFor(() => expect(h.logout).toHaveBeenCalled());
    await waitFor(() => expect(replace).toHaveBeenCalledWith("http://localhost/login"));
  });
});
