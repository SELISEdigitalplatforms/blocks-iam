import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  users: { data: undefined as { totalCount: number } | undefined, isLoading: false, isFetching: false },
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUsers: () => ({
    data: h.users.data,
    isLoading: h.users.isLoading,
    isFetching: h.users.isFetching,
  }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));

import { OrganizationMemberCount } from "./organization-member-count";

beforeEach(() => {
  vi.clearAllMocks();
  h.users = { data: undefined, isLoading: false, isFetching: false };
});

describe("OrganizationMemberCount", () => {
  it("renders a pulsing placeholder while loading", () => {
    h.users.isLoading = true;
    const { container } = render(<OrganizationMemberCount organizationId="o1" />);
    expect(container.querySelector("[class*='animate-pulse']")).not.toBeNull();
  });

  it("renders a pluralised member count", () => {
    h.users.data = { totalCount: 5 };
    render(<OrganizationMemberCount organizationId="o1" />);
    expect(screen.getByText("5 members")).toBeInTheDocument();
  });

  it("renders the singular form for exactly one member", () => {
    h.users.data = { totalCount: 1 };
    render(<OrganizationMemberCount organizationId="o1" />);
    expect(screen.getByText("1 member")).toBeInTheDocument();
  });

  it("defaults to zero members when there is no data", () => {
    render(<OrganizationMemberCount organizationId="o1" />);
    expect(screen.getByText("0 members")).toBeInTheDocument();
  });
});
