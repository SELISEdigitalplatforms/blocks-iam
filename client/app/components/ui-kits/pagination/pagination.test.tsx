import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { Pagination } from "./pagination";

const renderPagination = (totalCount: number, pageSize = 10) =>
  render(
    <Pagination
      page={0}
      pageSize={pageSize}
      totalCount={totalCount}
      onChange={vi.fn()}
      onPageSizeChange={vi.fn()}
      pageSizeOptions={[5, 10, 20, 40]}
    />,
  );

describe("Pagination rows-per-page", () => {
  it("hides the size choice when every size shows the same rows", () => {
    renderPagination(1, 5);
    expect(screen.queryByText("Rows per page")).not.toBeInTheDocument();
  });

  it("shows the size choice when a size changes what is shown", () => {
    renderPagination(12);
    expect(screen.getByText("Rows per page")).toBeInTheDocument();
  });

  it("hides the size choice when the smallest size already fits every row", () => {
    renderPagination(3, 10);
    expect(screen.queryByText("Rows per page")).not.toBeInTheDocument();
  });
});
