import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: unknown[]; totalCount: number },
  listProps: null as Record<string, unknown> | null,
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("./magic-urls-filter-toolbar", () => ({
  MagicUrlsFilterToolBar: () => <div data-testid="magic-toolbar" />,
  useMagicUrlsFilterQueryParams: () => ({
    queryParams: { page: 0, pageSize: 10, search: "", status: "" },
    setQueryParams: vi.fn(),
  }),
}));
vi.mock("./magic-urls-list", () => ({
  MagicUrlsList: (props: Record<string, unknown>) => {
    h.listProps = props;
    return <div data-testid="magic-list">rows:{(props.data as unknown[]).length}</div>;
  },
}));
vi.mock("@blocks-utilities/components/magic-url-dialog/magic-url-dialog", () => ({
  MagicUrlDialog: () => <div data-testid="shorten-dialog" />,
}));
vi.mock("@blocks-utilities/components/magic-url-config-dialog/magic-url-config-dialog", () => ({
  MagicUrlConfigDialog: () => <div data-testid="config-dialog" />,
}));
vi.mock("@blocks-utilities/hooks/use-magic-url", () => ({
  useGetMagicUrls: () => ({ data: h.data, isLoading: h.isLoading, isFetching: h.isFetching }),
  useSaveMagicUrlConfig: () => ({ mutateAsync: vi.fn() }),
}));

import { MagicUrls } from "./magic-urls";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
});

describe("MagicUrls", () => {
  it("renders the toolbar, dialogs and list", () => {
    h.data = { data: [{ id: 1 }, { id: 2 }], totalCount: 2 };
    render(<MagicUrls />);
    expect(screen.getByTestId("magic-toolbar")).toBeInTheDocument();
    expect(screen.getByTestId("shorten-dialog")).toBeInTheDocument();
    expect(screen.getByTestId("config-dialog")).toBeInTheDocument();
    expect(screen.getByTestId("magic-list")).toHaveTextContent("rows:2");
  });

  it("marks the list as loading while fetching", () => {
    h.isFetching = true;
    render(<MagicUrls />);
    expect(h.listProps?.isLoading).toBe(true);
  });
});
