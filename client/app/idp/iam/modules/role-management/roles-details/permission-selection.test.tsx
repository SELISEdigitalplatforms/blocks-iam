import { render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  getPermissions: vi.fn(),
}));

vi.mock("@blocks-idp/iam/services/permission.service", () => ({
  permissionService: { getPermissions: h.getPermissions },
}));
// The nested per-group fetch hook — kept idle (no group is open at first render).
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => ({ data: undefined, isLoading: false }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({
    selectedProject: { tenantId: "t1", itemId: "p1" },
    selectedTenantGroup: "tg1",
  })),
}));

import { PermissionSelection } from "./permission-selection";

const resourceGroups = [
  { resourceGroup: "Users", count: 4 },
  { resourceGroup: "Roles", count: 2 },
];

beforeEach(() => {
  vi.clearAllMocks();
  // The role's already-assigned permissions come back empty, so every group
  // renders "Selected: 0" without needing per-group data.
  h.getPermissions.mockResolvedValue({ data: [] });
});

describe("PermissionSelection", () => {
  it("renders an accordion entry per resource group once loaded", async () => {
    render(<PermissionSelection slug="admin" resourceGroups={resourceGroups} />, {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());
    expect(screen.getByText("Roles")).toBeInTheDocument();
    expect(h.getPermissions).toHaveBeenCalled();
  });

  it("shows the total and selected counts for each group", async () => {
    render(<PermissionSelection slug="admin" resourceGroups={resourceGroups} />, {
      wrapper: createWrapper(),
    });

    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());
    expect(screen.getByText(/Total Permissions: 4 \| Selected:\s*0/)).toBeInTheDocument();
    expect(screen.getByText(/Total Permissions: 2 \| Selected:\s*0/)).toBeInTheDocument();
  });
});
