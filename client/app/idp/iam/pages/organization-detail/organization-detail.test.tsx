import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({ orgResult: {} as Record<string, unknown> }));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useGetOrganizationById: () => h.orgResult,
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <div data-testid="breadcrumb" /> }));
vi.mock("@/constants/breadcrumb-custom-title", () => ({
  BREADCRUMB_CUSTOM_TITLES: {},
  BREADCRUMB_LINK_OVERRIDES: {},
}));
vi.mock("@blocks-idp/iam/modules/organization-management/organization-users", () => ({
  OrganizationUsers: ({ organizationId }: { organizationId: string }) => (
    <div data-testid="org-users">users:{organizationId}</div>
  ),
  InviteOrganizationUser: () => <div data-testid="invite-user" />,
}));

import { OrganizationDetail } from "./organization-detail";

beforeEach(() => {
  vi.clearAllMocks();
  h.orgResult = {
    data: {
      organization: {
        name: "Acme",
        isDisabled: false,
        email: "info@acme.com",
        phoneNumber: "123",
        websiteUrl: "https://acme.com",
        language: "en",
        addresses: [{ city: "NYC" }],
        createdDate: "2021-01-01",
        lastUpdatedDate: "2021-02-01",
      },
    },
    isLoading: false,
  };
});

describe("OrganizationDetail", () => {
  it("renders the organization name, active badge and info rows", () => {
    render(<OrganizationDetail id="org-1" />);
    expect(screen.getByText("Acme")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("info@acme.com")).toBeInTheDocument();
    expect(screen.getByText("1 configured")).toBeInTheDocument();
  });

  it("renders the disabled badge for a disabled organization", () => {
    (h.orgResult.data as { organization: Record<string, unknown> }).organization.isDisabled = true;
    render(<OrganizationDetail id="org-1" />);
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("renders the loading skeleton while the organization loads", () => {
    h.orgResult = { data: undefined, isLoading: true };
    render(<OrganizationDetail id="org-1" />);
    expect(screen.queryByText("Acme")).toBeNull();
    expect(screen.getByTestId("org-users")).toHaveTextContent("users:org-1");
  });
});
