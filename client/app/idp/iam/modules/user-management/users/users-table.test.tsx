import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({ navigate: vi.fn() }));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigate };
});
vi.mock("./users-filter-toolbar", () => ({
  useUsersSortQueryParams: () => ({
    sortQueryParams: { property: "FirstName", isDescending: false },
    setSortQueryParams: vi.fn(),
  }),
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: { SortHeader: ({ label }: { label: string }) => <span>{label}</span> },
}));
vi.mock("@/hooks/use-scoped-path", () => ({
  useScopedPath: () => (segment: string) => `/base/${segment}`,
}));
vi.mock("@/components/copy-to-clipboard-button", () => ({
  CopyToClipboardButton: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

import { UsersTable } from "./users-table";

const user = (over: Record<string, unknown> = {}) =>
  ({
    itemId: "u1",
    firstName: "Ada",
    lastName: "Lovelace",
    email: "ada@example.com",
    active: true,
    createdDate: "",
    lastUpdatedDate: "",
    lastLoggedInTime: "",
    ...over,
  }) as unknown as Parameters<typeof UsersTable>[0]["users"][number];

const renderTable = (props: Partial<Parameters<typeof UsersTable>[0]> = {}) =>
  render(
    <MemoryRouter>
      <UsersTable users={props.users ?? [user()]} isLoading={props.isLoading ?? false} />
    </MemoryRouter>,
  );

beforeEach(() => vi.clearAllMocks());

describe("UsersTable", () => {
  it("renders a row per user with name and status", () => {
    renderTable({ users: [user({ active: true })] });
    expect(screen.getByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
  });

  it("renders the inactive badge for inactive users", () => {
    renderTable({ users: [user({ active: false })] });
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows the empty state when there are no users", () => {
    renderTable({ users: [] });
    expect(screen.getByText("No users found.")).toBeInTheDocument();
  });

  it("renders the loading skeleton while loading", () => {
    const { container } = renderTable({ isLoading: true });
    expect(container.querySelector(".flex-col")).not.toBeNull();
    expect(screen.queryByText("Ada Lovelace")).toBeNull();
  });

  it("navigates to the user detail on row click", () => {
    renderTable({ users: [user({ itemId: "u9" })] });
    fireEvent.click(screen.getByText("Ada Lovelace"));
    expect(h.navigate).toHaveBeenCalledWith("/base/user-detail/u9");
  });
});
