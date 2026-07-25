import { render, screen } from "@testing-library/react";
import { beforeAll, beforeEach, describe, expect, it, vi } from "vitest";
import { LogsViewerContext } from "../logs-viewer";
import type { ILog } from "../../models/log.model";

const h = vi.hoisted(() => ({
  logs: {
    initialLogs: [] as ILog[],
    isLoading: false,
    hasTopMore: false,
    fetchOldLogs: vi.fn().mockResolvedValue([]),
    fetchNewLogs: vi.fn().mockResolvedValue([]),
  },
}));

vi.mock("../../hooks/use-logs", () => ({ useLogs: () => h.logs }));
vi.mock("./log-item", () => ({
  LogItem: ({ log }: { log: ILog }) => <span>log-{log.traceId}</span>,
}));
vi.mock("../logs-header/logs-filter-toolbar", () => ({
  LogsFilterToolbar: () => <div data-testid="filter-toolbar" />,
}));

import { LogsList } from "./logs-list";

beforeAll(() => {
  if (typeof Element.prototype.scrollTo !== "function") {
    Element.prototype.scrollTo = () => {};
  }
});

const contextValue = {
  selectedService: { serviceName: "auth" },
  filter: { level: "", startDate: "", endDate: "", search: "" },
  pageSize: 20,
} as unknown as React.ContextType<typeof LogsViewerContext>;

const renderList = () =>
  render(
    <LogsViewerContext.Provider value={contextValue}>
      <LogsList />
    </LogsViewerContext.Provider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.logs = {
    initialLogs: [],
    isLoading: false,
    hasTopMore: false,
    fetchOldLogs: vi.fn().mockResolvedValue([]),
    fetchNewLogs: vi.fn().mockResolvedValue([]),
  };
});

describe("LogsList", () => {
  it("renders the filter toolbar", () => {
    renderList();
    expect(screen.getByTestId("filter-toolbar")).toBeInTheDocument();
  });

  it("shows the loading skeletons while logs are loading", () => {
    h.logs = { ...h.logs, isLoading: true };
    const { container } = renderList();
    expect(container.querySelectorAll("[class*='rounded']").length).toBeGreaterThan(0);
    expect(screen.queryByText(/^log-/)).toBeNull();
  });

  it("renders a log item per initial log", () => {
    h.logs = {
      ...h.logs,
      isLoading: false,
      initialLogs: [
        { traceId: "t1", timestamp: "2025-01-01T00:00:00Z" },
        { traceId: "t2", timestamp: "2025-01-02T00:00:00Z" },
      ] as ILog[],
    };
    renderList();
    expect(screen.getByText("log-t1")).toBeInTheDocument();
    expect(screen.getByText("log-t2")).toBeInTheDocument();
  });
});
