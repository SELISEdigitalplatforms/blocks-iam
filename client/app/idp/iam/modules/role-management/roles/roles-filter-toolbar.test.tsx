import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  setQueryParams: vi.fn(),
  queryParams: {} as Record<string, string>,
}));

vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  parseAsString: { withDefault: (d: string) => ({ _d: d }) },
  useQueryStates: () => [h.queryParams, h.setQueryParams],
}));
vi.mock("@/components/filter-toolbar", () => ({
  FilterToolbar: ({
    onChange,
    onReset,
  }: {
    onChange: (key: string, value: unknown) => void;
    onReset: () => void;
  }) => (
    <div>
      <button onClick={() => onChange("search", "abc")}>change-search</button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
  useSortQueryParams: () => ({ sortQueryParams: {}, setSortQueryParams: vi.fn() }),
}));

import { RolesFilterToolBar } from "./roles-filter-toolbar";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { search: "" };
});

describe("RolesFilterToolBar", () => {
  it("updates the search query param and resets the page", () => {
    render(<RolesFilterToolBar />);
    fireEvent.click(screen.getByText("change-search"));
    expect(h.setQueryParams).toHaveBeenCalledTimes(1);
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({ search: "", page: 3 })).toEqual({ search: "abc", page: 0 });
  });

  it("resets all query params", () => {
    render(<RolesFilterToolBar />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
