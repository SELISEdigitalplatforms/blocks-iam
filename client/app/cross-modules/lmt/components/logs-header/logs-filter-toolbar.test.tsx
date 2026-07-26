import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { LogsViewerContext } from "../logs-viewer";

// Cut the http-client chain (logs-viewer -> logs-list -> use-logs -> lmt.service)
// so this toolbar test does not pull the design-system HttpClient into the graph.
vi.mock("../../hooks/use-logs", () => ({
  useLogs: () => ({
    initialLogs: [],
    isLoading: false,
    hasTopMore: false,
    fetchOldLogs: vi.fn().mockResolvedValue([]),
    fetchNewLogs: vi.fn().mockResolvedValue([]),
  }),
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
          onChange("date", {
            from: new Date("2020-01-01T00:00:00.000Z"),
            to: new Date("2020-02-01T00:00:00.000Z"),
          })
        }
      >
        change-date
      </button>
      <button onClick={() => onChange("date", null)}>clear-date</button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
}));

import { LogsFilterToolbar } from "./logs-filter-toolbar";

const setFilter = vi.fn();
const resetFilter = vi.fn();

const renderToolbar = (filter: unknown = { level: "", startDate: "", endDate: "", search: "" }) =>
  render(
    <LogsViewerContext.Provider
      value={
        {
          filter,
          setFilter,
          resetFilter,
        } as unknown as React.ContextType<typeof LogsViewerContext>
      }
    >
      <LogsFilterToolbar />
    </LogsViewerContext.Provider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
});

describe("LogsFilterToolbar", () => {
  it("updates a generic filter key", () => {
    renderToolbar();
    fireEvent.click(screen.getByText("change-search"));
    const updater = setFilter.mock.calls[0][0] as (f: object) => object;
    expect(updater({ level: "" })).toEqual({ level: "", search: "abc" });
  });

  it("maps a date range to iso start and end dates", () => {
    renderToolbar();
    fireEvent.click(screen.getByText("change-date"));
    const updater = setFilter.mock.calls[0][0] as (f: object) => object;
    expect(updater({})).toEqual({
      startDate: "2020-01-01T00:00:00.000Z",
      endDate: "2020-02-01T00:00:00.000Z",
    });
  });

  it("clears the dates when the range is empty", () => {
    renderToolbar();
    fireEvent.click(screen.getByText("clear-date"));
    const updater = setFilter.mock.calls[0][0] as (f: object) => object;
    expect(updater({})).toEqual({ startDate: "", endDate: "" });
  });

  it("resets the filter", () => {
    renderToolbar();
    fireEvent.click(screen.getByText("reset"));
    expect(resetFilter).toHaveBeenCalled();
  });

  it("falls back to empty defaults when the filter context is null", () => {
    expect(() => renderToolbar(null)).not.toThrow();
    expect(screen.getByText("change-search")).toBeInTheDocument();
  });
});
