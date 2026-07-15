import { renderHook } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import type { Table } from "@tanstack/react-table";
import type { DateRange } from "react-day-picker";
import { useActiveFiltersCount } from "./use-active-filters-count";

// Minimal Table stub exposing only what the hook reads.
const makeTable = (columnFilters: Array<{ id: string; value: unknown }>) =>
  ({ getState: () => ({ columnFilters }) }) as unknown as Table<unknown>;

describe("useActiveFiltersCount", () => {
  it("returns 0 with no filters and no date range", () => {
    const { result } = renderHook(() => useActiveFiltersCount(makeTable([]), undefined, undefined));
    expect(result.current).toBe(0);
  });

  it("counts scalar filter values as one each", () => {
    const table = makeTable([
      { id: "status", value: "active" },
      { id: "name", value: "" },
    ]);
    const { result } = renderHook(() => useActiveFiltersCount(table, undefined, undefined));
    expect(result.current).toBe(1);
  });

  it("counts each entry of an array filter value", () => {
    const table = makeTable([{ id: "roles", value: ["admin", "editor"] }]);
    const { result } = renderHook(() => useActiveFiltersCount(table, undefined, undefined));
    expect(result.current).toBe(2);
  });

  it("counts object keys for object filter values", () => {
    const table = makeTable([{ id: "range", value: { min: 1, max: 5 } }]);
    const { result } = renderHook(() => useActiveFiltersCount(table, undefined, undefined));
    expect(result.current).toBe(2);
  });

  it("counts the 'types' array for the search column", () => {
    const table = makeTable([{ id: "search", value: { types: ["a", "b", "c"] } }]);
    const { result } = renderHook(() => useActiveFiltersCount(table, undefined, "search"));
    expect(result.current).toBe(3);
  });

  it("adds one when a date range boundary is set", () => {
    const table = makeTable([]);
    const dateRange = { from: new Date() } as DateRange;
    const { result } = renderHook(() => useActiveFiltersCount(table, dateRange, undefined));
    expect(result.current).toBe(1);
  });
});
