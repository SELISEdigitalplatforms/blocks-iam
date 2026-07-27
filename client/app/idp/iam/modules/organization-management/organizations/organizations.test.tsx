import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  orgsResult: {} as Record<string, unknown>,
  configResult: {} as Record<string, unknown>,
  selectedProject: { tenantId: "tenant-1" } as { tenantId: string } | null,
}));

vi.mock("nuqs", () => ({
  useQueryState: (_key: string, opts: { defaultValue: string }) => {
    return [opts.defaultValue, vi.fn()];
  },
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizations: () => h.orgsResult,
  useGetOrganizationConfig: () => h.configResult,
}));
vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: h.selectedProject }),
}));
vi.mock("./organizations-filter-toolbar", () => ({
  useOrganizationsSortQueryParams: () => ({
    sortQueryParams: { property: "Name", isDescending: false },
  }),
}));
vi.mock("../organization-config", () => ({
  OrganizationConfig: ({ trigger }: { trigger: React.ReactNode }) => (
    <div>{trigger}</div>
  ),
}));
vi.mock("./organizations-sidebar-list", () => ({
  OrganizationsSidebarList: ({ organizations }: { organizations: { itemId: string }[] }) => (
    <div data-testid="sidebar">sidebar:{organizations.length}</div>
  ),
}));
vi.mock("./organization-workspace-panel", () => ({
  OrganizationWorkspacePanel: ({ organizationId }: { organizationId: string }) => (
    <div data-testid="workspace">workspace:{organizationId}</div>
  ),
}));

import { Organizations } from "./organizations";

beforeEach(() => {
  vi.clearAllMocks();
  h.selectedProject = { tenantId: "tenant-1" };
  h.orgsResult = {
    data: {
      isSuccess: true,
      organizations: [{ itemId: "o1", name: "Acme" }],
      totalCount: 1,
    },
    isLoading: false,
    isFetching: false,
  };
  h.configResult = {
    data: { isMultiOrgEnabled: true },
    isLoading: false,
  };
});

describe("Organizations", () => {
  it("renders the sidebar list and workspace when organizations load", () => {
    render(<Organizations />);
    expect(screen.getByTestId("sidebar")).toHaveTextContent("sidebar:1");
  });

  it("shows the multi-org-disabled card when the list reports the error", () => {
    h.orgsResult = {
      data: { isSuccess: false, errors: { multi_org_disabled: true } },
      isLoading: false,
      isFetching: false,
    };
    render(<Organizations />);
    expect(screen.getByText("Multiple Organizations is not enabled")).toBeInTheDocument();
    expect(screen.getByText("Configure Organization")).toBeInTheDocument();
  });

  it("shows the multi-org-disabled card when config reports it disabled", () => {
    h.configResult = { data: { isMultiOrgEnabled: false }, isLoading: false };
    render(<Organizations />);
    expect(screen.getByText("Multiple Organizations is not enabled")).toBeInTheDocument();
  });
});
