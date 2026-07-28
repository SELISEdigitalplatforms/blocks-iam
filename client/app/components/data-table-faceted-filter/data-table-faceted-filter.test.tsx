import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { DataTableFacetedFilter } from "./data-table-faceted-filter";

const options = [
  { label: "Alpha", value: "a" },
  { label: "Beta", value: "b" },
];

const makeColumn = (types: string[] = []) => ({
  getFacetedUniqueValues: () => new Map([["a", 3]]),
  getFilterValue: () => (types.length ? { types } : undefined),
  setFilterValue: vi.fn(),
});

beforeEach(() => {
  vi.clearAllMocks();
});

describe("DataTableFacetedFilter", () => {
  it("renders the title on the trigger", () => {
    render(
      <DataTableFacetedFilter column={makeColumn() as never} title="Kind" options={options} />,
    );
    expect(screen.getByRole("button", { name: /Kind/ })).toBeInTheDocument();
  });

  it("adds a value to the column filter when an option is selected", () => {
    const column = makeColumn();
    render(
      <DataTableFacetedFilter column={column as never} title="Kind" options={options} />,
    );
    fireEvent.click(screen.getByRole("button", { name: /Kind/ }));
    fireEvent.click(screen.getByText("Beta"));
    expect(column.setFilterValue).toHaveBeenCalledWith({ types: ["b"] });
  });

  it("shows selected badges and clears the filter", () => {
    const column = makeColumn(["a"]);
    render(
      <DataTableFacetedFilter column={column as never} title="Kind" options={options} />,
    );
    // Selected value shows as a badge on the trigger.
    expect(screen.getAllByText("Alpha").length).toBeGreaterThan(0);
    fireEvent.click(screen.getByRole("button", { name: /Kind/ }));
    fireEvent.click(screen.getByText("Clear"));
    expect(column.setFilterValue).toHaveBeenCalledWith(undefined);
  });
});
