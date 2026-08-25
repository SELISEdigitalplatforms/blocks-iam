import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
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
vi.mock("../update-role/update-role", () => ({
  UpdateRole: () => null,
}));

import { RolesList } from "./roles-list";

type Row = Parameters<typeof RolesList>[0]["roles"][number];

const role = (over: Record<string, unknown>) =>
  ({
    itemId: "r1",
    name: "Manager",
    slug: "manager",
    count: 0,
    description: "d",
    ...over,
  }) as unknown as Row;

const renderList = (roles: Row[], showDefaultOriginBadge: boolean) =>
  render(
    <MemoryRouter>
      <RolesList roles={roles} isLoading={false} showDefaultOriginBadge={showDefaultOriginBadge} />
    </MemoryRouter>,
  );

describe("RolesList — default-origin badge", () => {
  it("marks a role that came from the default organization", () => {
    renderList([role({ createdFromDefault: true })], true);

    expect(screen.getByText("Default")).toBeInTheDocument();
  });

  it("leaves an organization's own role unmarked", () => {
    renderList([role({ slug: "manager_f47ac10b", createdFromDefault: false })], true);

    expect(screen.queryByText("Default")).not.toBeInTheDocument();
  });

  it("marks only the copy when both are listed together", () => {
    renderList(
      [
        role({ itemId: "r1", createdFromDefault: true }),
        role({ itemId: "r2", slug: "manager_f47ac10b", createdFromDefault: false }),
      ],
      true,
    );

    expect(screen.getAllByText("Default")).toHaveLength(1);
    expect(screen.getAllByText("Manager")).toHaveLength(2);
  });

  it("renders no badge in a single-organization tenant", () => {
    renderList([role({ createdFromDefault: true })], false);

    expect(screen.queryByText("Default")).not.toBeInTheDocument();
  });

  it("treats a role predating the field as not default-derived", () => {
    // createdFromDefault absent entirely, as on documents written before it existed.
    renderList([role({})], true);

    expect(screen.queryByText("Default")).not.toBeInTheDocument();
  });

  it("still renders the role name alongside the badge", () => {
    renderList([role({ createdFromDefault: true })], true);

    expect(screen.getByText("Manager")).toBeInTheDocument();
    expect(screen.getByText("manager")).toBeInTheDocument();
  });
});
