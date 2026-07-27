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
      <button
        onClick={() =>
          onChange("expiryDate", {
            from: new Date("2020-01-01T00:00:00.000Z"),
            to: new Date("2020-02-01T00:00:00.000Z"),
          })
        }
      >
        change-expiry
      </button>
      <button onClick={() => onChange("expiryDate", null)}>clear-expiry</button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
  useSortQueryParams: () => ({ sortQueryParams: {}, setSortQueryParams: vi.fn() }),
}));

import { MagicUrlsFilterToolBar } from "./magic-urls-filter-toolbar";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = {
    search: "",
    expiryStartDate: "",
    expiryEndDate: "",
    status: "",
    requestMethod: "",
    type: "",
  };
});

describe("MagicUrlsFilterToolBar", () => {
  it("updates a generic filter key", () => {
    render(<MagicUrlsFilterToolBar />);
    fireEvent.click(screen.getByText("change-search"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({ search: "abc", page: 0 });
  });

  it("converts an expiry date range to iso start and end dates", () => {
    render(<MagicUrlsFilterToolBar />);
    fireEvent.click(screen.getByText("change-expiry"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({
      expiryStartDate: "2020-01-01T00:00:00.000Z",
      expiryEndDate: "2020-02-01T00:00:00.000Z",
      page: 0,
    });
  });

  it("clears the expiry dates when the range is empty", () => {
    render(<MagicUrlsFilterToolBar />);
    fireEvent.click(screen.getByText("clear-expiry"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({
      expiryStartDate: "",
      expiryEndDate: "",
      page: 0,
    });
  });

  it("resets all query params", () => {
    render(<MagicUrlsFilterToolBar />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
