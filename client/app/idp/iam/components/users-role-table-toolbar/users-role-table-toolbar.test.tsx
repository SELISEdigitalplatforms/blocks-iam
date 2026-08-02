import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { Table } from "@tanstack/react-table";

vi.mock("@/components/data-table-faceted-filter/data-table-faceted-filter", () => ({
  DataTableFacetedFilter: () => <div data-testid="faceted-filter" />,
}));
vi.mock("@/components/date-range-filter/date-range-filter", () => ({
  DateRangeFilter: () => <div data-testid="date-range-filter" />,
}));
vi.mock("@blocks-localization/models/language", () => ({ translation: [] }));
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

import { UsersRoleTableToolbar } from "./users-role-table-toolbar";

const nameColumn = { setFilterValue: vi.fn(), getFilterValue: vi.fn() };

const makeTable = (columnFilters: Array<{ id: string; value: unknown }> = []) =>
  ({
    getColumn: (id: string) => (id === "name" ? nameColumn : undefined),
    getState: () => ({ columnFilters }),
    resetColumnFilters: vi.fn(),
  }) as unknown as Table<unknown> & { resetColumnFilters: ReturnType<typeof vi.fn> };

beforeEach(() => vi.clearAllMocks());

describe("UsersRoleTableToolbar", () => {
  it("renders the search input", () => {
    render(<UsersRoleTableToolbar table={makeTable()} />);
    expect(screen.getAllByPlaceholderText("Filter users by name or email").length).toBeGreaterThan(0);
  });

  it("pushes typed text into the name column filter", () => {
    render(<UsersRoleTableToolbar table={makeTable()} />);
    const input = screen.getAllByPlaceholderText("Filter users by name or email")[0];
    fireEvent.change(input, { target: { value: "john" } });
    expect(nameColumn.setFilterValue).toHaveBeenCalledWith("john");
  });

  it("does not show the reset button when nothing is filtered", () => {
    render(<UsersRoleTableToolbar table={makeTable()} />);
    expect(screen.queryByRole("button", { name: /reset/i })).not.toBeInTheDocument();
  });

  it("resets column filters when an active filter is present", () => {
    const table = makeTable([{ id: "status", value: "active" }]);
    render(<UsersRoleTableToolbar table={table} />);
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    expect(table.resetColumnFilters).toHaveBeenCalledTimes(1);
  });
});
