import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: unknown[]; totalCount: number },
  queryParams: {} as Record<string, string | number>,
  lastQuery: null as Record<string, unknown> | null,
  tableProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUsers: (q: Record<string, unknown>) => {
    h.lastQuery = q;
    return { isLoading: h.isLoading, isFetching: h.isFetching, data: h.data };
  },
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./users-table", () => ({
  UsersTable: (props: Record<string, unknown>) => {
    h.tableProps = props;
    return <div data-testid="users-table">rows:{(props.users as unknown[]).length}</div>;
  },
}));
vi.mock("./users-filter-toolbar", () => ({
  UsersSearchFilter: () => <div data-testid="search-filter" />,
  UsersDateFilters: () => <div data-testid="date-filter" />,
  useUsersFilterQueryParams: () => ({ queryParams: h.queryParams, setQueryParams: vi.fn() }),
  useUsersSortQueryParams: () => ({ sortQueryParams: {} }),
}));

import { Users } from "./users";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
  h.queryParams = {
    page: 0,
    pageSize: 10,
    "selected-filter": "name",
    name: "alice",
    email: "a@b.co",
  };
});

describe("Users", () => {
  it("renders the search and date filters plus the users table", () => {
    h.data = { data: [{ id: 1 }, { id: 2 }], totalCount: 2 };
    render(<Users />);
    expect(screen.getByTestId("search-filter")).toBeInTheDocument();
    expect(screen.getByTestId("date-filter")).toBeInTheDocument();
    expect(screen.getByTestId("users-table")).toHaveTextContent("rows:2");
  });

  it("uses the name as query text when the name filter is selected", () => {
    render(<Users />);
    expect(h.lastQuery?.query).toBe("alice");
  });

  it("uses the email as query text when the email filter is selected", () => {
    h.queryParams["selected-filter"] = "email";
    render(<Users />);
    expect(h.lastQuery?.query).toBe("a@b.co");
  });

  it("marks the table as loading while fetching", () => {
    h.isFetching = true;
    render(<Users />);
    expect(h.tableProps?.isLoading).toBe(true);
  });
});
