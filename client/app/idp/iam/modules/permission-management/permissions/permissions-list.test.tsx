import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  navigateMock: vi.fn(),
  setSortQueryParams: vi.fn(),
}));

vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => h.navigateMock };
});
// The sort-query-params hook pulls in nuqs (needs an adapter); the scoped-path
// hook reaches into the real design-system package. Stub both.
vi.mock("./permissions-filter-toolbar", () => ({
  usePermissionsSortQuaryParams: () => ({
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

import { PermissionsList } from "./permissions-list";

type PermOverrides = Partial<{
  itemId: string;
  name: string;
  resource: string;
  isBuiltIn: boolean;
  type: number;
  permissionSeverity: number;
  roles: string[];
  tags: string[];
  description: string;
}>;

const perm = (over: PermOverrides) =>
  ({
    itemId: "p1",
    name: "Read Users",
    resource: "users:read",
    isBuiltIn: false,
    type: 1,
    permissionSeverity: 2,
    roles: [],
    tags: [],
    description: "reads users",
    resourceGroup: "Users",
    dependentPermissions: [],
    ...over,
  }) as unknown as Parameters<typeof PermissionsList>[0]["permissions"][number];

const renderList = (props: Partial<Parameters<typeof PermissionsList>[0]> = {}) =>
  render(
    <MemoryRouter>
      <PermissionsList permissions={props.permissions ?? []} isLoading={props.isLoading ?? false} />
    </MemoryRouter>,
  );

beforeEach(() => vi.clearAllMocks());

describe("PermissionsList", () => {
  it("renders a row per permission with name, resource and source badge", () => {
    renderList({
      permissions: [
        perm({ itemId: "p1", name: "Read Users", resource: "users:read", isBuiltIn: true }),
        perm({ itemId: "p2", name: "Edit Roles", resource: "roles:edit", isBuiltIn: false }),
      ],
    });
    expect(screen.getByText("Read Users")).toBeInTheDocument();
    expect(screen.getByText("users:read")).toBeInTheDocument();
    expect(screen.getByText("Edit Roles")).toBeInTheDocument();
    expect(screen.getByText("Built In")).toBeInTheDocument();
    expect(screen.getByText("Custom")).toBeInTheDocument();
  });

  it("shows the empty state when there are no permissions", () => {
    renderList({ permissions: [] });
    expect(
      screen.getByText("No permission found. Please create new permission."),
    ).toBeInTheDocument();
  });

  it("navigates to the permission detail when a row is clicked", () => {
    renderList({ permissions: [perm({ itemId: "p9", name: "Delete Data" })] });
    fireEvent.click(screen.getByText("Delete Data"));
    expect(h.navigateMock).toHaveBeenCalledWith("/base/permission-detail/p9");
  });

  it("renders a loading skeleton while loading", () => {
    const { container } = renderList({ permissions: [], isLoading: true });
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("shows the first tag and an overflow count badge", () => {
    renderList({ permissions: [perm({ itemId: "p1", tags: ["alpha", "beta", "gamma"] })] });
    expect(screen.getByText("alpha")).toBeInTheDocument();
    expect(screen.getByText("2+")).toBeInTheDocument();
  });

  it("renders an edit link only for custom permissions", () => {
    renderList({
      permissions: [
        perm({ itemId: "custom", name: "Custom Perm", isBuiltIn: false }),
        perm({ itemId: "builtin", name: "Builtin Perm", isBuiltIn: true }),
      ],
    });
    const links = Array.from(document.querySelectorAll("a")).map((a) => a.getAttribute("href"));
    expect(links).toContain("/base/permission-detail/custom");
    expect(links).not.toContain("/base/permission-detail/builtin");
  });
});
