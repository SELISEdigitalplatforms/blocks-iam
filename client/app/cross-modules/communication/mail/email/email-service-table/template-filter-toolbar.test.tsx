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
    onChange: (key: string, value: unknown, values: unknown) => void;
    onReset: () => void;
  }) => (
    <div>
      <button
        onClick={() =>
          onChange("search", "abc", {
            search: "abc",
            language: "en",
            mailConfigurationId: "cfg1",
          })
        }
      >
        change
      </button>
      <button onClick={onReset}>reset</button>
    </div>
  ),
  useSortQueryParams: () => ({ sortQueryParams: {}, setSortQueryParams: vi.fn() }),
}));

import { TemplateFilterToolbar } from "./template-filter-toolbar";

const emailConfigsData = [{ itemId: "cfg1", name: "Config One" }];
const languageListData = [
  { itemId: "l1", languageName: "English", languageCode: "en", isDefault: true },
];

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { search: "", language: "", mailConfigurationId: "" };
});

describe("TemplateFilterToolbar", () => {
  it("applies the full values object and resets the page number on change", () => {
    render(
      <TemplateFilterToolbar
        emailConfigsData={emailConfigsData}
        languageListData={languageListData}
      />,
    );
    fireEvent.click(screen.getByText("change"));
    const updater = h.setQueryParams.mock.calls[0][0] as (p: object) => object;
    expect(updater({ pageNumber: 4 })).toEqual({
      pageNumber: 0,
      search: "abc",
      language: "en",
      mailConfigurationId: "cfg1",
    });
  });

  it("resets all query params", () => {
    render(
      <TemplateFilterToolbar
        emailConfigsData={emailConfigsData}
        languageListData={languageListData}
      />,
    );
    fireEvent.click(screen.getByText("reset"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });
});
