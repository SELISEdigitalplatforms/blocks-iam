import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ filtersCount: 0, isServiceBarOpen: false }));

vi.mock("@/hooks/use-is-mobile", () => ({ default: () => false }));
vi.mock("@/hooks/use-active-filters-count", () => ({
  useActiveFiltersCount: () => h.filtersCount,
}));
vi.mock("@blocks-communication/mail/hooks/use-is-service-tab-open-comm", () => ({
  default: () => h.isServiceBarOpen,
}));
vi.mock("@/components/search-input/search-input", () => ({
  SearchInput: ({ onSearch }: { onSearch: (v: string) => void }) => (
    <button onClick={() => onSearch("abc")}>do-search</button>
  ),
}));
vi.mock("@/components/data-table-faceted-filter/data-table-faceted-filter", () => ({
  DataTableFacetedFilter: ({ title }: { title: string }) => <div>facet-{title}</div>,
}));

import { MessagingTableToolbar } from "./messaging-table-toolbar";

const makeTable = () => {
  const columns: Record<string, { setFilterValue: ReturnType<typeof vi.fn> }> = {
    name: { setFilterValue: vi.fn() },
    configuration: { setFilterValue: vi.fn() },
    protocol: { setFilterValue: vi.fn() },
  };
  return {
    getColumn: (id: string) => columns[id],
    resetColumnFilters: vi.fn(),
    _columns: columns,
  };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.filtersCount = 0;
  h.isServiceBarOpen = false;
});

describe("MessagingTableToolbar", () => {
  it("renders the search and faceted filters in the desktop view", () => {
    const table = makeTable();
    render(<MessagingTableToolbar table={table as never} />);
    expect(screen.getAllByText("do-search").length).toBeGreaterThan(0);
    expect(screen.getByText("facet-Configuration")).toBeInTheDocument();
    expect(screen.getByText("facet-Protocol")).toBeInTheDocument();
  });

  it("pushes the search term into the name column filter", () => {
    const table = makeTable();
    render(<MessagingTableToolbar table={table as never} />);
    fireEvent.click(screen.getAllByText("do-search")[0]);
    expect(table._columns.name.setFilterValue).toHaveBeenCalledWith("abc");
  });

  it("shows a reset button that clears the column filters when filtered", () => {
    h.filtersCount = 2;
    const table = makeTable();
    render(<MessagingTableToolbar table={table as never} />);
    fireEvent.click(screen.getAllByRole("button", { name: /Reset/ })[0]);
    expect(table.resetColumnFilters).toHaveBeenCalled();
  });
});
