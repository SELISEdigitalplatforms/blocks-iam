import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: unknown[]; totalCount: number },
  setQueryParams: vi.fn(),
  tableProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUsers: () => ({ isLoading: h.isLoading, isFetching: h.isFetching, data: h.data }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./organization-users-filter-toolbar", () => ({
  OrganizationUsersFilterToolbar: () => <div data-testid="filter-toolbar" />,
  useOrganizationUsersFilterQueryParams: () => ({
    queryParams: { page: 0, pageSize: 5, email: "", name: "" },
    setQueryParams: h.setQueryParams,
  }),
  useOrganizationUsersSortQueryParams: () => ({ sortQueryParams: {} }),
}));
vi.mock("./organization-users-table", () => ({
  OrganizationUsersTable: (props: Record<string, unknown>) => {
    h.tableProps = props;
    return <div data-testid="users-table">rows:{(props.users as unknown[]).length}</div>;
  },
}));

import { OrganizationUsers } from "./organization-users";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
});

describe("OrganizationUsers", () => {
  it("renders the title, description and filter toolbar", () => {
    render(
      <OrganizationUsers organizationId="o1" title="Members" description="All members" />,
    );
    expect(screen.getByText("Members")).toBeInTheDocument();
    expect(screen.getByText("All members")).toBeInTheDocument();
    expect(screen.getByTestId("filter-toolbar")).toBeInTheDocument();
  });

  it("passes fetched users to the table", () => {
    h.data = { data: [{ id: 1 }, { id: 2 }], totalCount: 2 };
    render(<OrganizationUsers organizationId="o1" />);
    expect(screen.getByTestId("users-table")).toHaveTextContent("rows:2");
  });

  it("shows the members range summary in the footer when there are results", () => {
    h.data = { data: [{ id: 1 }], totalCount: 12 };
    render(<OrganizationUsers organizationId="o1" />);
    expect(screen.getByText(/Showing 1.*of 12 members/)).toBeInTheDocument();
  });

  it("marks the table as loading while fetching", () => {
    h.isFetching = true;
    render(<OrganizationUsers organizationId="o1" />);
    expect(h.tableProps?.isLoading).toBe(true);
  });
});
