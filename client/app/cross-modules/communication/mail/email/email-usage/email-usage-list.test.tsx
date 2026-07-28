import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";

const h = vi.hoisted(() => ({
  usageResult: {} as Record<string, unknown>,
  queryParams: { page: 0, pageSize: 10, search: "", status: "", startDate: "", endDate: "" },
}));

vi.mock("@blocks-communication/mail/hooks/use-email-usage", () => ({
  useGetEmailUsage: () => h.usageResult,
}));
vi.mock("@blocks-communication/mail/email/email-usage/status-badge", () => ({
  StatusBadge: ({ status }: { status: string }) => <span>status-{status}</span>,
}));
vi.mock("@blocks-communication/mail/email/email-usage/email-usage-filter-toolbar", () => ({
  EmailUsageFilterToolbar: () => <div data-testid="usage-filter" />,
  useEmailUsageFilterQueryParams: () => ({ queryParams: h.queryParams, setQueryParams: vi.fn() }),
}));

import { EmailUsageList } from "./email-usage-list";

const renderList = (isInbound = false) =>
  render(
    <MemoryRouter>
      <EmailUsageList isInbound={isInbound} />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.usageResult = {
    data: {
      data: [
        { messageId: "m1", from: "a@x.com", to: "b@y.com", subject: "Hello", status: "sent", date: "2021-01-01" },
      ],
      totalCount: 1,
    },
    isLoading: false,
  };
});

describe("EmailUsageList", () => {
  it("renders a row per email with the status badge (outbound)", () => {
    renderList(false);
    expect(screen.getByText("Hello")).toBeInTheDocument();
    expect(screen.getByText("status-sent")).toBeInTheDocument();
    expect(screen.getByText("Send Date")).toBeInTheDocument();
  });

  it("omits the status column and uses Received Date header for inbound", () => {
    renderList(true);
    expect(screen.getByText("Received Date")).toBeInTheDocument();
    expect(screen.queryByText("status-sent")).toBeNull();
  });

  it("renders the loading skeleton while loading", () => {
    h.usageResult = { data: undefined, isLoading: true };
    const { container } = renderList();
    expect(container.querySelector(".grid")).not.toBeNull();
  });

  it("shows the no-results row when there is no data", () => {
    h.usageResult = { data: { data: [], totalCount: 0 }, isLoading: false };
    renderList();
    expect(screen.getByText("No results.")).toBeInTheDocument();
  });
});
