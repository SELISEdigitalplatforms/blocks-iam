import { render, screen, fireEvent, renderHook, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  queryParams: {} as Record<string, unknown>,
  setQueryParams: vi.fn(),
}));

vi.mock("nuqs", () => ({
  parseAsString: { withDefault: (d: string) => ({ _d: d }) },
  parseAsBoolean: { withDefault: (d: boolean) => ({ _d: d }) },
  useQueryStates: () => [h.queryParams, h.setQueryParams],
}));

import { SortHeader, useSortQueryParams } from "./sort-header";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { "sort-property": "Name", "sort-isDescending": false };
});

describe("SortHeader", () => {
  it("sets ascending on a newly selected column", () => {
    const onChange = vi.fn();
    render(
      <SortHeader id="Email" label="Email" value={{ property: "Name", isDescending: false }} onChange={onChange} />,
    );
    fireEvent.click(screen.getByText("Email"));
    expect(onChange).toHaveBeenCalledWith({ property: "Email", isDescending: false });
  });

  it("toggles the sort direction on the active column", () => {
    const onChange = vi.fn();
    render(
      <SortHeader id="Name" label="Name" value={{ property: "Name", isDescending: false }} onChange={onChange} />,
    );
    fireEvent.click(screen.getByText("Name"));
    expect(onChange).toHaveBeenCalledWith({ property: "Name", isDescending: true });
  });
});

describe("useSortQueryParams", () => {
  it("exposes the current sort params from the query state", () => {
    const { result } = renderHook(() => useSortQueryParams({ initial: { property: "Name", isDescending: false } }));
    expect(result.current.sortQueryParams).toEqual({ property: "Name", isDescending: false });
  });

  it("writes the sort params through the query state setter", () => {
    const { result } = renderHook(() => useSortQueryParams({ initial: { property: "Name", isDescending: false } }));
    act(() => result.current.setSortQueryParams({ property: "Email", isDescending: true }));
    expect(h.setQueryParams).toHaveBeenCalled();
    const updater = h.setQueryParams.mock.calls[0][0] as () => object;
    expect(updater()).toEqual({ "sort-isDescending": true, "sort-property": "Email" });
  });

  it("resets the sort params", () => {
    const { result } = renderHook(() => useSortQueryParams({ initial: { property: "Name", isDescending: false } }));
    act(() => result.current.reset());
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
