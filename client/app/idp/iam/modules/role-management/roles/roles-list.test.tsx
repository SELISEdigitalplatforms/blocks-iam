import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";

const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setSortQueryParams: vi.fn(),
}));

vi.mock("react-router", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
vi.mock("./roles-filter-toolbar", () => ({
  useRolesSortQueryParams: () => ({
    sortQueryParams: { property: "Name", isDescending: false },
    setSortQueryParams: h.setSortQueryParams,
  }),
}));
vi.mock("@/hooks/use-scoped-path", () => ({
  useScopedPath: () => (segment: string) => `/base/${segment}`,
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterControls: {
    SortHeader: ({ label }: { label: string }) => <span>{label}</span>,
  },
}));
// UpdateRole reaches into mutation hooks and the design-system package; render
// a marker so the edit-dialog branch is exercised without its dependency graph.
vi.mock("../update-role/update-role", () => ({
  UpdateRole: ({ isOpen }: { isOpen: boolean }) =>
    isOpen ? <div>update-role-dialog</div> : null,
}));

import { RolesList } from "./roles-list";

type RoleOverrides = Partial<{
  itemId: string;
  name: string;
  slug: string;
  count: number;
  description: string;
}>;

const role = (over: RoleOverrides) =>
  ({
    itemId: "r1",
    name: "Admin",
    slug: "admin",
    count: 3,
    description: "administrator",
    ...over,
  }) as unknown as Parameters<typeof RolesList>[0]["roles"][number];

const renderList = (props: Partial<Parameters<typeof RolesList>[0]> = {}) =>
  render(
    <MemoryRouter>
      <RolesList roles={props.roles ?? []} isLoading={props.isLoading ?? false} />
    </MemoryRouter>,
  );

beforeEach(() => vi.clearAllMocks());

describe("RolesList", () => {
  it("renders a row per role with name, slug and count", () => {
    renderList({
      roles: [
        role({ itemId: "r1", name: "Admin", slug: "admin", count: 5 }),
        role({ itemId: "r2", name: "Viewer", slug: "viewer", count: 1 }),
      ],
    });
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("admin")).toBeInTheDocument();
    expect(screen.getByText("Viewer")).toBeInTheDocument();
    expect(screen.getByText("viewer")).toBeInTheDocument();
  });

  it("shows the empty state when there are no roles", () => {
    renderList({ roles: [] });
    expect(
      screen.getByText("No roles found. Please create new roles."),
    ).toBeInTheDocument();
  });

  it("renders the loading skeleton when isLoading is true", () => {
    const { container } = renderList({ isLoading: true });
    expect(container.querySelector(".grid")).not.toBeNull();
    expect(screen.queryByText("No roles found. Please create new roles.")).toBeNull();
  });

  it("navigates to the role detail when a row is clicked", () => {
    renderList({ roles: [role({ itemId: "r9", name: "Editor" })] });
    fireEvent.click(screen.getByText("Editor"));
    expect(h.navigateMock).toHaveBeenCalledWith("/base/role-detail/r9");
  });

  it("opens the edit dialog when the pencil action is clicked without navigating", () => {
    renderList({ roles: [role({ itemId: "r3", name: "Ops" })] });
    const editButton = document.querySelector("button");
    fireEvent.click(editButton as Element);
    expect(screen.getByText("update-role-dialog")).toBeInTheDocument();
    expect(h.navigateMock).not.toHaveBeenCalled();
  });
});
