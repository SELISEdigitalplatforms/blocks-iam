import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: unknown[]; totalCount: number },
  setQueryParams: vi.fn(),
  listProps: null as Record<string, unknown> | null,
  lastQuery: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: (q: Record<string, unknown>) => {
    h.lastQuery = q;
    return { isLoading: h.isLoading, isFetching: h.isFetching, data: h.data };
  },
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./permissions-list", () => ({
  PermissionsList: (props: Record<string, unknown>) => {
    h.listProps = props;
    return <div data-testid="permissions-list">rows:{(props.permissions as unknown[]).length}</div>;
  },
}));
vi.mock("./permissions-filter-toolbar", () => ({
  PermissionsFilterToolbar: () => <div data-testid="filter-toolbar" />,
  usePermissionsFilterQuaryParams: () => ({
    queryParams: { page: 0, pageSize: 10, search: "", isBuiltIn: "yes", type: "2" },
    setQueryParams: h.setQueryParams,
  }),
  usePermissionsSortQuaryParams: () => ({ sortQueryParams: {} }),
}));
vi.mock("./permissions-group-severity", () => ({
  PermissionsGroupBySeverity: () => <div data-testid="group-severity" />,
}));

import { Permissions } from "./permissions";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
});

describe("Permissions", () => {
  it("renders the severity summary, toolbar and permissions list", () => {
    h.data = { data: [{ id: 1 }, { id: 2 }], totalCount: 2 };
    render(<Permissions />);
    expect(screen.getByTestId("group-severity")).toBeInTheDocument();
    expect(screen.getByTestId("filter-toolbar")).toBeInTheDocument();
    expect(screen.getByTestId("permissions-list")).toHaveTextContent("rows:2");
  });

  it("normalises the isBuiltIn and numeric type filters for the query", () => {
    render(<Permissions />);
    expect(h.lastQuery?.isBuiltIn).toBe("yes");
    expect(h.lastQuery?.type).toBe(2);
  });

  it("marks the list as loading while fetching", () => {
    h.isFetching = true;
    render(<Permissions />);
    expect(h.listProps?.isLoading).toBe(true);
  });
});
