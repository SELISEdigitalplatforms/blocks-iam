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
      <button onClick={() => onChange("type", "1")}>change-type</button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
  useSortQueryParams: () => ({ sortQueryParams: {}, setSortQueryParams: vi.fn() }),
}));

import { PermissionsFilterToolbar } from "./permissions-filter-toolbar";

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { search: "", isBuiltIn: "", type: "", permissionSeverity: "" };
});

describe("PermissionsFilterToolbar", () => {
  it("updates a filter query param and resets the page", () => {
    render(<PermissionsFilterToolbar />);
    fireEvent.click(screen.getByText("change-type"));
    expect(h.setQueryParams).toHaveBeenCalledTimes(1);
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({ type: "", page: 5 })).toEqual({ type: "1", page: 0 });
  });

  it("resets all query params", () => {
    render(<PermissionsFilterToolbar />);
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
