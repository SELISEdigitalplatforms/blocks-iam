import { fireEvent, render, screen } from "@testing-library/react";
import type { Table } from "@tanstack/react-table";
import { describe, expect, it, vi } from "vitest";
import { TablePagination } from "./table-pagination";

type Overrides = Partial<Record<string, unknown>>;

function makeTable(over: Overrides = {}) {
  return {
    getFilteredRowModel: () => ({ rows: new Array(25).fill({}) }),
    getFilteredSelectedRowModel: () => ({ rows: [] }),
    getState: () => ({ pagination: { pageSize: 10, pageIndex: 0 } }),
    getPageCount: () => 3,
    getCanPreviousPage: () => false,
    getCanNextPage: () => true,
    setPageIndex: vi.fn(),
    setPageSize: vi.fn(),
    previousPage: vi.fn(),
    nextPage: vi.fn(),
    ...over,
  } as unknown as Table<unknown>;
}

describe("TablePagination", () => {
  it("shows the total count summary and the current page", () => {
    render(<TablePagination table={makeTable()} totalCount={5} />);
    expect(screen.getByText(/Total 5 items/)).toBeInTheDocument();
    expect(screen.getByText(/Page 1 of 3/)).toBeInTheDocument();
  });

  it("shows the selected-rows summary when no totalCount is given", () => {
    render(<TablePagination table={makeTable()} />);
    expect(screen.getByText(/0 of 25 row\(s\) selected\./)).toBeInTheDocument();
  });

  it("disables the previous controls and drives the next/last controls", () => {
    const nextPage = vi.fn();
    const setPageIndex = vi.fn();
    const onPageChange = vi.fn();
    const table = makeTable({ nextPage, setPageIndex });
    render(<TablePagination table={table} onPageChange={onPageChange} />);

    const buttons = Array.from(document.querySelectorAll("button")).filter((b) =>
      b.classList.contains("w-8"),
    );
    // [first, prev, next, last]
    expect(buttons[0]).toBeDisabled();
    expect(buttons[1]).toBeDisabled();

    fireEvent.click(buttons[2]); // next
    expect(nextPage).toHaveBeenCalled();

    fireEvent.click(buttons[3]); // last
    expect(setPageIndex).toHaveBeenCalledWith(2);
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("jumps to the first page from the first-page control when enabled", () => {
    const setPageIndex = vi.fn();
    const onPageChange = vi.fn();
    const table = makeTable({
      getCanPreviousPage: () => true,
      getState: () => ({ pagination: { pageSize: 10, pageIndex: 2 } }),
      setPageIndex,
    });
    render(<TablePagination table={table} onPageChange={onPageChange} />);
    const buttons = Array.from(document.querySelectorAll("button")).filter((b) =>
      b.classList.contains("w-8"),
    );
    fireEvent.click(buttons[0]); // first page
    expect(setPageIndex).toHaveBeenCalledWith(0);
    expect(onPageChange).toHaveBeenCalledWith(0);
  });
});
