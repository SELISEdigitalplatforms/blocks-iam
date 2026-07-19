import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { OrganizationsSidebarList } from "./organizations-sidebar-list";

// The component is purely presentational — every piece of data and every
// callback arrives via props, so no module mocks are required.

type OrgOverrides = Partial<{
  itemId: string;
  name: string;
  isDisabled: boolean;
  lastUpdatedDate: string;
  logoUrl: string | null;
}>;

// Cast through unknown: the component only reads a handful of IOrganization
// fields, so an exhaustive object is unnecessary noise in the test.
const makeOrg = (over: OrgOverrides) =>
  ({
    itemId: "org-1",
    name: "Acme Inc",
    isDisabled: false,
    lastUpdatedDate: "2026-07-01T00:00:00.000Z",
    logoUrl: null,
    ...over,
  }) as unknown as Parameters<typeof OrganizationsSidebarList>[0]["organizations"][number];

const baseProps = () => ({
  organizations: [
    makeOrg({ itemId: "org-1", name: "Acme Inc" }),
    makeOrg({ itemId: "org-2", name: "Globex" }),
  ],
  totalCount: 2,
  selectedOrgId: null as string | null,
  onSelect: vi.fn(),
  search: "",
  onSearchChange: vi.fn(),
  isLoading: false,
  isLoadingMore: false,
  hasMore: false,
  onLoadMore: vi.fn(),
});

beforeEach(() => vi.clearAllMocks());

describe("OrganizationsSidebarList", () => {
  it("renders every organization and the footer count", () => {
    render(<OrganizationsSidebarList {...baseProps()} />);
    expect(screen.getByText("Acme Inc")).toBeInTheDocument();
    expect(screen.getByText("Globex")).toBeInTheDocument();
    expect(screen.getByText(/Showing 1 to 2 of 2 organizations/)).toBeInTheDocument();
  });

  it("shows the empty state when there are no organizations", () => {
    render(<OrganizationsSidebarList {...baseProps()} organizations={[]} totalCount={0} />);
    expect(screen.getByText("No organizations found")).toBeInTheDocument();
    expect(screen.queryByText("Acme Inc")).not.toBeInTheDocument();
  });

  it("invokes onSelect with the clicked organization", () => {
    const props = baseProps();
    render(<OrganizationsSidebarList {...props} />);
    fireEvent.click(screen.getByText("Globex"));
    expect(props.onSelect).toHaveBeenCalledTimes(1);
    expect(props.onSelect.mock.calls[0][0].itemId).toBe("org-2");
  });

  it("renders skeletons and hides the footer while loading", () => {
    render(<OrganizationsSidebarList {...baseProps()} isLoading />);
    expect(screen.queryByText("Acme Inc")).not.toBeInTheDocument();
    expect(screen.queryByText(/Showing 1 to/)).not.toBeInTheDocument();
  });

  it("clears the search when the clear button is pressed", () => {
    const props = baseProps();
    render(<OrganizationsSidebarList {...props} />);
    const input = screen.getByPlaceholderText("Search organizations...");
    fireEvent.change(input, { target: { value: "acme" } });
    const clear = screen.getByLabelText("Clear search");
    fireEvent.click(clear);
    expect(props.onSearchChange).toHaveBeenCalledWith("");
  });
});
