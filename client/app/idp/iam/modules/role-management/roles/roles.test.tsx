import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: unknown[]; totalCount: number },
  listProps: null as Record<string, unknown> | null,
  isMultiOrgEnabled: false,
}));

vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoles: () => ({ data: h.data, isLoading: h.isLoading, isFetching: h.isFetching }),
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizationConfig: () => ({ data: { isMultiOrgEnabled: h.isMultiOrgEnabled } }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./roles-list", () => ({
  RolesList: (props: Record<string, unknown>) => {
    h.listProps = props;
    return <div data-testid="roles-list">rows:{(props.roles as unknown[]).length}</div>;
  },
}));
vi.mock("./roles-filter-toolbar", () => ({
  RolesFilterToolBar: () => <div data-testid="roles-toolbar" />,
  useRolesFilterQueryParams: () => ({
    queryParams: { page: 0, pageSize: 10, search: "" },
    setQueryParams: vi.fn(),
  }),
  useRolesSortQueryParams: () => ({ sortQueryParams: {} }),
}));

import { Roles } from "./roles";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
});

describe("Roles", () => {
  it("renders the toolbar and roles list", () => {
    h.data = { data: [{ id: 1 }, { id: 2 }], totalCount: 2 };
    render(<Roles />);
    expect(screen.getByTestId("roles-toolbar")).toBeInTheDocument();
    expect(screen.getByTestId("roles-list")).toHaveTextContent("rows:2");
  });

  it("marks the list as loading while fetching", () => {
    h.isFetching = true;
    render(<Roles />);
    expect(h.listProps?.isLoading).toBe(true);
  });

  it("does not render pagination when total fits one page", () => {
    h.data = { data: [{ id: 1 }], totalCount: 1 };
    const { container } = render(<Roles />);
    expect(container.textContent).not.toMatch(/Showing/);
  });
});
