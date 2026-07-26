import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { createRef } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";
import type { IPermission } from "@blocks-idp/iam/models/permission";

const h = vi.hoisted(() => ({
  getPermissions: vi.fn(),
  groupState: { data: undefined as unknown, isLoading: false },
}));

vi.mock("@blocks-idp/iam/services/permission.service", () => ({
  permissionService: { getPermissions: h.getPermissions },
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissions: () => ({
    data: h.groupState.data,
    isLoading: h.groupState.isLoading,
  }),
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

function perm(overrides: Partial<IPermission>): IPermission {
  return {
    itemId: "id",
    name: "name",
    type: 1,
    description: "",
    resource: "res",
    resourceGroup: "Users",
    projectKey: "t1",
    tags: [],
    roles: [],
    dependentPermissions: [],
    isArchived: false,
    isBuiltIn: false,
    language: null,
    organizationIds: [],
    permissionSeverity: 0 as unknown as IPermission["permissionSeverity"],
    ...overrides,
  };
}

const fa = perm({
  itemId: "fa1",
  name: "Manage Users",
  type: 2,
  resource: "faR",
  dependentPermissions: ["depR"],
});
const dep = perm({ itemId: "dep1", name: "Read Dep", type: 1, resource: "depR" });
const ind = perm({ itemId: "ind1", name: "Independent", type: 1, resource: "indR" });

beforeEach(() => {
  vi.clearAllMocks();
  h.getPermissions.mockResolvedValue({ data: [] });
  h.groupState.data = undefined;
  h.groupState.isLoading = false;
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

  it("renders the loading skeleton while the role permissions are pending", () => {
    h.getPermissions.mockReturnValue(new Promise(() => {}));
    const { container } = render(
      <PermissionSelection slug="admin" resourceGroups={resourceGroups} />,
      { wrapper: createWrapper() },
    );
    expect(container.querySelector(".animate-pulse")).not.toBeNull();
    expect(screen.queryByText("Users")).not.toBeInTheDocument();
  });

  it("loads group permissions when a group is expanded and toggles them", async () => {
    h.groupState.data = { data: [fa, dep, ind] };
    render(<PermissionSelection slug="admin" resourceGroups={resourceGroups} />, {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());

    fireEvent.click(screen.getByText("Users"));

    await waitFor(() => expect(screen.getByText("Manage Users")).toBeInTheDocument());
    expect(screen.getByText("Read Dep")).toBeInTheDocument();
    expect(screen.getByText("Independent")).toBeInTheDocument();
    expect(screen.getByText("FE Action")).toBeInTheDocument();

    fireEvent.click(document.getElementById("perm-fa1") as HTMLElement);
    await waitFor(() =>
      expect(document.getElementById("perm-fa1")).toHaveAttribute(
        "data-state",
        "checked",
      ),
    );
    expect(document.getElementById("perm-dep1")).toHaveAttribute("data-state", "checked");

    fireEvent.click(document.getElementById("perm-fa1") as HTMLElement);
    await waitFor(() =>
      expect(document.getElementById("perm-fa1")).toHaveAttribute(
        "data-state",
        "unchecked",
      ),
    );
  });

  it("toggles a dependent permission and syncs its parent FE action", async () => {
    h.groupState.data = { data: [fa, dep, ind] };
    render(<PermissionSelection slug="admin" resourceGroups={resourceGroups} />, {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());
    fireEvent.click(screen.getByText("Users"));
    await waitFor(() => expect(screen.getByText("Read Dep")).toBeInTheDocument());

    fireEvent.click(document.getElementById("perm-dep1") as HTMLElement);
    await waitFor(() =>
      expect(document.getElementById("perm-dep1")).toHaveAttribute(
        "data-state",
        "checked",
      ),
    );
    expect(document.getElementById("perm-fa1")).toHaveAttribute("data-state", "checked");

    fireEvent.click(document.getElementById("perm-dep1") as HTMLElement);
    await waitFor(() =>
      expect(document.getElementById("perm-fa1")).toHaveAttribute(
        "data-state",
        "unchecked",
      ),
    );

    fireEvent.click(document.getElementById("perm-ind1") as HTMLElement);
    await waitFor(() =>
      expect(document.getElementById("perm-ind1")).toHaveAttribute(
        "data-state",
        "checked",
      ),
    );
  });

  it("selects and clears an entire group with the group checkbox", async () => {
    h.groupState.data = { data: [fa, dep, ind] };
    render(<PermissionSelection slug="admin" resourceGroups={resourceGroups} />, {
      wrapper: createWrapper(),
    });
    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());
    fireEvent.click(screen.getByText("Users"));
    await waitFor(() => expect(screen.getByText("Manage Users")).toBeInTheDocument());

    // The group checkbox lives in the always-visible header, so the selected
    // count in the header is the reliable signal (the content may collapse).
    const groupCheckbox = document.getElementById("group-Users") as HTMLElement;
    fireEvent.click(groupCheckbox);
    await waitFor(() => expect(screen.getByText(/Selected:\s*3/)).toBeInTheDocument());

    fireEvent.click(groupCheckbox);
    await waitFor(() =>
      expect(screen.getAllByText(/Selected:\s*0/).length).toBeGreaterThan(0),
    );
  });

  it("exposes handleSave via ref returning added and removed permissions", async () => {
    h.getPermissions.mockResolvedValue({ data: [ind] });
    h.groupState.data = { data: [fa, dep, ind] };
    const ref = createRef<{ handleSave: () => unknown }>();
    render(
      <PermissionSelection ref={ref} slug="admin" resourceGroups={resourceGroups} />,
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(screen.getByText("Users")).toBeInTheDocument());
    fireEvent.click(screen.getByText("Users"));
    await waitFor(() => expect(screen.getByText("Manage Users")).toBeInTheDocument());

    fireEvent.click(document.getElementById("perm-fa1") as HTMLElement);
    fireEvent.click(document.getElementById("perm-ind1") as HTMLElement);

    await waitFor(() =>
      expect(document.getElementById("perm-fa1")).toHaveAttribute(
        "data-state",
        "checked",
      ),
    );

    const result = ref.current?.handleSave() as {
      addedPermissions: IPermission[];
      removedPermissions: IPermission[];
    };
    const addedIds = result.addedPermissions.map((p) => p.itemId);
    const removedIds = result.removedPermissions.map((p) => p.itemId);
    expect(addedIds).toContain("fa1");
    expect(removedIds).toContain("ind1");
  });
});
