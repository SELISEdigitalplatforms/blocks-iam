import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { data: [], totalCount: 0 } as { data: { itemId: string }[]; totalCount: number },
}));

vi.mock("nuqs", () => ({
  parseAsInteger: { withDefault: (d: number) => ({ _d: d }) },
  useQueryStates: () => [{ page: 0, pageSize: 10 }, vi.fn()],
}));
vi.mock("@blocks-identifier/hooks/use-services", () => ({
  useGetAllServices: () => ({ data: h.data, isLoading: h.isLoading, isFetching: h.isFetching }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));
vi.mock("@blocks-identifier/components/service-card/service-card", () => ({
  ServiceCard: ({ service }: { service: { itemId: string } }) => (
    <div data-testid="service-card">{service.itemId}</div>
  ),
}));

import { ServiceList } from "./service-list";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { data: [], totalCount: 0 };
});

describe("ServiceList", () => {
  it("renders a loading skeleton while fetching", () => {
    h.isLoading = true;
    const { container } = render(<ServiceList />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the empty state when there are no services", () => {
    render(<ServiceList />);
    expect(screen.getByText("No services found")).toBeInTheDocument();
  });

  it("renders a card per service", () => {
    h.data = {
      data: [{ itemId: "s1" }, { itemId: "s2" }],
      totalCount: 2,
    };
    render(<ServiceList />);
    expect(screen.getAllByTestId("service-card")).toHaveLength(2);
  });
});
