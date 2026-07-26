import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ setQueryParams: vi.fn(), queryParams: {} as Record<string, string> }));

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
      <button onClick={() => onChange("search", { selected: "email", value: "abc" })}>
        change-search
      </button>
      <button onClick={() => onChange("joinedOn", { from: new Date("2020-01-01"), to: undefined })}>
        change-date
      </button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
  useSortQueryParams: () => ({ sortQueryParams: {}, setSortQueryParams: vi.fn() }),
}));

import {
  UsersSearchFilter,
  UsersDateFilters,
  rangeToIso,
  isoToRange,
} from "./users-filter-toolbar";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { "selected-filter": "name", name: "", email: "" };
});

describe("users-filter-toolbar helpers", () => {
  it("rangeToIso converts dates and strings to iso, undefined otherwise", () => {
    expect(rangeToIso(null)).toEqual({ from: undefined, to: undefined });
    const d = new Date("2021-05-01T00:00:00.000Z");
    expect(rangeToIso({ from: d, to: "2021-06-01" })).toEqual({
      from: d.toISOString(),
      to: "2021-06-01",
    });
  });

  it("isoToRange parses iso strings into Date objects", () => {
    const r = isoToRange("2021-05-01", "");
    expect(r.from).toBeInstanceOf(Date);
    expect(r.to).toBeUndefined();
  });
});

describe("UsersSearchFilter", () => {
  it("updates the query params when the search filter changes", () => {
    render(<UsersSearchFilter />);
    fireEvent.click(screen.getByText("change-search"));
    expect(h.setQueryParams).toHaveBeenCalled();
  });

  it("resets the query params", () => {
    render(<UsersSearchFilter />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});

describe("UsersDateFilters", () => {
  it("updates the range query params when a date filter changes", () => {
    render(<UsersDateFilters />);
    fireEvent.click(screen.getByText("change-date"));
    expect(h.setQueryParams).toHaveBeenCalled();
  });

  it("resets the date query params", () => {
    render(<UsersDateFilters />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
