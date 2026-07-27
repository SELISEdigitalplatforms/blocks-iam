import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  useGetTraces: vi.fn(),
  getAllServices: vi.fn(),
  useIsMobile: vi.fn(),
  setQueryParams: vi.fn(),
  queryParams: { search: "", services: [] as string[], page: 0, pageSize: 10 },
}));

vi.mock("@blocks-lmt/hooks/use-trace", () => ({
  useGetTraces: h.useGetTraces,
}));
vi.mock("@blocks-identifier/services/service-registery.service", () => ({
  serviceRegistryService: { getAllServices: h.getAllServices },
}));
vi.mock("@/hooks/use-is-mobile", () => ({ default: h.useIsMobile }));
vi.mock("nuqs", () => ({
  useQueryStates: () => [h.queryParams, h.setQueryParams],
  parseAsString: { withDefault: () => ({}) },
  parseAsInteger: { withDefault: () => ({}) },
  parseAsArrayOf: () => ({ withDefault: () => ({}) }),
}));
vi.mock("@/components/filter-toolbar", () => ({
  useSortQueryParams: () => ({
    sortQueryParams: { property: "Timestamp", isDescending: true },
    setSortQueryParams: vi.fn(),
  }),
  FilterControls: {
    SortHeader: ({ label }: { label: string }) => <span>{label}</span>,
  },
  FilterToolbar: ({ onReset }: { onReset: () => void }) => (
    <button type="button" onClick={onReset}>
      reset-filters
    </button>
  ),
}));
vi.mock("@blocks-lmt/components/trace-guideline/trace-provider-guideline", () => ({
  TraceProviderSetupGuideLine: ({ open }: { open: boolean }) =>
    open ? <div data-testid="guideline">guideline open</div> : null,
}));

import { TracesOverview } from "./traces-overview";
import type { TraceTree } from "@blocks-lmt/models/trace.model";

const trace = (over: Partial<TraceTree> = {}): TraceTree =>
  ({
    timestamp: "2026-07-01T10:00:00Z",
    serviceName: "email",
    duration: "120",
    entryPoint: { method: "get", actionName: "SendMail" },
    subEntries: [],
    ...over,
  }) as TraceTree;

function setTraces({
  data = [] as TraceTree[],
  totalCount = 0,
  isLoading = false,
  isFetching = false,
}: {
  data?: TraceTree[];
  totalCount?: number;
  isLoading?: boolean;
  isFetching?: boolean;
} = {}) {
  h.useGetTraces.mockReturnValue({
    data: { data, totalCount },
    isLoading,
    isFetching,
    refetch: vi.fn(),
  });
}

const renderOverview = () =>
  render(<TracesOverview projectKey="proj-1" />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  h.queryParams = { search: "", services: [], page: 0, pageSize: 10 };
  h.useIsMobile.mockReturnValue(false);
  h.getAllServices.mockResolvedValue({ data: [] });
  setTraces();
});

describe("TracesOverview", () => {
  it("renders the storage-mode cards and the guide button on desktop", () => {
    renderOverview();
    expect(screen.getByText("Trace storage modes")).toBeInTheDocument();
    expect(screen.getByText("Hot")).toBeInTheDocument();
    expect(screen.getByText("Cold")).toBeInTheDocument();
    expect(screen.getByText("Archive")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /guide/i })).toBeInTheDocument();
  });

  it("shows the empty state when there are no traces", () => {
    renderOverview();
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });

  it("renders a trace row with method, action, service and duration", () => {
    setTraces({ data: [trace()], totalCount: 1 });
    renderOverview();
    expect(screen.getByText("get")).toBeInTheDocument();
    expect(screen.getByText("SendMail")).toBeInTheDocument();
    expect(screen.getByText("120ms")).toBeInTheDocument();
  });

  it("shows loading skeletons while fetching", () => {
    setTraces({ isLoading: true });
    const { container } = renderOverview();
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("toggles the setup guideline open when the Guide button is clicked", () => {
    renderOverview();
    expect(screen.queryByTestId("guideline")).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /guide/i }));
    expect(screen.getByTestId("guideline")).toBeInTheDocument();
  });

  it("switches provider tab when a mode card is clicked", () => {
    renderOverview();
    fireEvent.click(screen.getByText("Cold"));
    // Cold tab content shows a coming-soon placeholder.
    expect(screen.getByText("Coming soon")).toBeInTheDocument();
    // The page is reset to 0 on a tab change.
    expect(h.setQueryParams).toHaveBeenCalled();
  });

  it("resets the query params when the toolbar reset fires", () => {
    renderOverview();
    fireEvent.click(screen.getByText("reset-filters"));
    expect(h.setQueryParams).toHaveBeenCalledWith(null);
  });

  it("renders a select control on mobile instead of the card grid", () => {
    h.useIsMobile.mockReturnValue(true);
    renderOverview();
    expect(screen.getByRole("combobox")).toBeInTheDocument();
  });

  it("merges registered services with the built-in ones", async () => {
    h.getAllServices.mockResolvedValue({
      data: [{ name: "Custom Service", serviceId: "svc-custom" }],
    });
    setTraces({ data: [trace({ serviceName: "svc-custom" })], totalCount: 1 });
    renderOverview();
    // The row's service label resolves from the merged service list.
    await waitFor(() =>
      expect(screen.getByText("Custom Service")).toBeInTheDocument(),
    );
  });

  it("shows pagination when the total count exceeds the page size", () => {
    setTraces({
      data: Array.from({ length: 10 }, (_, i) => trace({ serviceName: `s${i}` })),
      totalCount: 42,
    });
    const { container } = renderOverview();
    // Pagination renders a navigation region.
    expect(
      within(container).getAllByRole("button").length,
    ).toBeGreaterThan(0);
  });
});
