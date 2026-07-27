import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  config: undefined as { isMultiOrgEnabled: boolean } | undefined,
  isConfigLoading: false,
  user: undefined as { data: Record<string, unknown> } | undefined,
  isUserLoading: false,
}));

vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizationConfig: () => ({ data: h.config, isLoading: h.isConfigLoading }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => ({ data: h.user, isLoading: h.isUserLoading }),
}));
vi.mock("./single-org-access", () => ({
  SingleOrgAccess: () => <div data-testid="single-org" />,
}));
vi.mock("./multi-org-access", () => ({
  MultiOrgAccess: () => <div data-testid="multi-org" />,
}));

import { UserAccessTab } from "./user-access-tab";

beforeEach(() => {
  vi.clearAllMocks();
  h.config = { isMultiOrgEnabled: false };
  h.isConfigLoading = false;
  h.user = { data: { organizationIds: [] } };
  h.isUserLoading = false;
});

describe("UserAccessTab", () => {
  it("renders skeletons while config or user data loads", () => {
    h.isConfigLoading = true;
    const { container } = render(<UserAccessTab userId="u1" projectKey="p1" />);
    expect(container.querySelectorAll("[class*='animate-pulse']").length).toBeGreaterThan(0);
  });

  it("renders the multi-org view when multi-org is enabled", () => {
    h.config = { isMultiOrgEnabled: true };
    render(<UserAccessTab userId="u1" projectKey="p1" />);
    expect(screen.getByTestId("multi-org")).toBeInTheDocument();
  });

  it("renders the multi-org view when the user belongs to organizations", () => {
    h.user = { data: { organizationIds: ["o1", "o2"] } };
    render(<UserAccessTab userId="u1" projectKey="p1" />);
    expect(screen.getByTestId("multi-org")).toBeInTheDocument();
  });

  it("renders the single-org view when multi-org is disabled and no orgs exist", () => {
    render(<UserAccessTab userId="u1" projectKey="p1" />);
    expect(screen.getByTestId("single-org")).toBeInTheDocument();
  });
});
