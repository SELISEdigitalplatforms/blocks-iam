import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  setQueryParams: vi.fn(),
  queryParams: {} as Record<string, string>,
  filters: [] as Array<{ key: string; type: string; label: string }>,
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
    filters,
  }: {
    onChange: (key: string, value: unknown) => void;
    onReset: () => void;
    filters: Array<{ key: string; type: string; label: string }>;
  }) => {
    h.filters = filters;
    return (
      <div>
        <button onClick={() => onChange("search", "abc")}>change-search</button>
        <button
          onClick={() =>
            onChange("sendDate", {
              from: new Date("2020-01-01T00:00:00.000Z"),
              to: new Date("2020-02-01T00:00:00.000Z"),
            })
          }
        >
          change-date
        </button>
        <button onClick={() => onChange("sendDate", null)}>clear-date</button>
        <button onClick={onReset}>reset</button>
      </div>
    );
  },
}));

import { EmailUsageFilterToolbar } from "./email-usage-filter-toolbar";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { search: "", startDate: "", endDate: "", status: "" };
  h.filters = [];
});

describe("EmailUsageFilterToolbar", () => {
  it("includes a status filter and Send Date label for outbound usage", () => {
    render(<EmailUsageFilterToolbar isInbound={false} />);
    expect(h.filters.some((f) => f.key === "status")).toBe(true);
    expect(h.filters.some((f) => f.label === "Send Date")).toBe(true);
  });

  it("omits the status filter and uses Received Date label for inbound usage", () => {
    render(<EmailUsageFilterToolbar isInbound={true} />);
    expect(h.filters.some((f) => f.key === "status")).toBe(false);
    expect(h.filters.some((f) => f.label === "Received Date")).toBe(true);
  });

  it("updates a generic filter key", () => {
    render(<EmailUsageFilterToolbar isInbound={false} />);
    fireEvent.click(screen.getByText("change-search"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({ search: "abc", page: 0 });
  });

  it("converts a send date range to iso start and end dates", () => {
    render(<EmailUsageFilterToolbar isInbound={false} />);
    fireEvent.click(screen.getByText("change-date"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({
      startDate: "2020-01-01T00:00:00.000Z",
      endDate: "2020-02-01T00:00:00.000Z",
      page: 0,
    });
  });

  it("clears the send dates when the range is empty", () => {
    render(<EmailUsageFilterToolbar isInbound={false} />);
    fireEvent.click(screen.getByText("clear-date"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({})).toEqual({ startDate: "", endDate: "", page: 0 });
  });

  it("resets all query params", () => {
    render(<EmailUsageFilterToolbar isInbound={false} />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
