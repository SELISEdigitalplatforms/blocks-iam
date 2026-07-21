import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Table } from "@tanstack/react-table";

vi.mock("@/components/data-table-faceted-filter/data-table-faceted-filter", () => ({
  DataTableFacetedFilter: () => <div data-testid="faceted-filter" />,
}));
vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));
vi.mock("@blocks-localization/hooks/use-is-service-tab-open-local", () => ({
  default: () => false,
}));
vi.mock("@/components/search-input/search-input", () => ({
  SearchInput: ({
    onSearch,
    placeholder,
    value,
  }: {
    onSearch: (t: string) => void;
    placeholder: string;
    value: string;
  }) => (
    <input
      aria-label={placeholder}
      placeholder={placeholder}
      value={value}
      onChange={(e) => onSearch(e.target.value)}
    />
  ),
}));

import { UsersRolePermissionTableToolbar } from "./users-role-permission-table-toolbar";

const nameColumn = { setFilterValue: vi.fn(), getFilterValue: vi.fn() };

const makeTable = (columnFilters: Array<{ id: string; value: unknown }> = []) =>
  ({
    getColumn: (id: string) => (id === "name" ? nameColumn : {}),
    getState: () => ({ columnFilters }),
    getRowModel: () => ({
      rows: [
        { original: { resourceGroup: "Users" } },
        { original: { resourceGroup: "Roles" } },
      ],
    }),
    resetColumnFilters: vi.fn(),
  }) as unknown as Table<{ resourceGroup: string }> & {
    resetColumnFilters: ReturnType<typeof vi.fn>;
  };

beforeEach(() => vi.clearAllMocks());

describe("UsersRolePermissionTableToolbar", () => {
  it("renders without crashing and shows the permission filter input", () => {
    render(<UsersRolePermissionTableToolbar table={makeTable()} />);
    expect(screen.getAllByPlaceholderText(/Filter/).length).toBeGreaterThan(0);
  });

  it("pushes typed text into the name column filter", () => {
    render(<UsersRolePermissionTableToolbar table={makeTable()} />);
    const input = screen.getAllByPlaceholderText(/Filter/)[0];
    fireEvent.change(input, { target: { value: "read" } });
    expect(nameColumn.setFilterValue).toHaveBeenCalledWith("read");
  });

  it("resets column filters when an active filter is present", () => {
    const table = makeTable([{ id: "resourceGroup", value: ["Users"] }]);
    render(<UsersRolePermissionTableToolbar table={table} />);
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    expect(table.resetColumnFilters).toHaveBeenCalledTimes(1);
  });
});
