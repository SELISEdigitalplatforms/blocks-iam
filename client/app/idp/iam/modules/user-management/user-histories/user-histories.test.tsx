import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  isLoading: false,
  isFetching: false,
  data: { items: [], totalCount: 0 } as { items: unknown[]; totalCount: number },
  listProps: null as Record<string, unknown> | null,
}));

vi.mock("@blocks-idp/iam/security/hooks", () => ({
  useActivities: () => ({ isLoading: h.isLoading, isFetching: h.isFetching, data: h.data }),
}));
vi.mock("@blocks-idp/iam/security/mappers/activity.mapper", () => ({
  toActivityRowViewModel: (item: { itemId: string }) => ({ id: item.itemId }),
}));
vi.mock("@blocks-idp/iam/security/components/activity-list", () => ({
  ActivityList: (props: Record<string, unknown>) => {
    h.listProps = props;
    return <div data-testid="activity-list">rows:{(props.rows as unknown[]).length}</div>;
  },
}));

import { UserHistories } from "./user-histories";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.isFetching = false;
  h.data = { items: [], totalCount: 0 };
});

describe("UserHistories", () => {
  it("maps activity items into rows for the list", () => {
    h.data = { items: [{ itemId: "a" }, { itemId: "b" }], totalCount: 2 };
    render(<UserHistories id="u1" projectKey="p1" />);
    expect(screen.getByTestId("activity-list")).toHaveTextContent("rows:2");
  });

  it("marks the list as loading while fetching", () => {
    h.isFetching = true;
    render(<UserHistories id="u1" projectKey="p1" />);
    expect(h.listProps?.isLoading).toBe(true);
  });
});
