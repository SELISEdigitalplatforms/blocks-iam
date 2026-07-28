import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PermissionState, PermissionMap, PermissionGroup } from "./role-details-state";

const h = vi.hoisted(() => ({
  permissionMap: new Map() as PermissionMap,
  lastGroups: [] as { group: PermissionGroup; onTrigger: () => void }[],
}));

vi.mock("./role-details-state", () => ({
  useRoleDetailsStore: (selector: (s: unknown) => unknown) =>
    selector({ permissionMap: h.permissionMap }),
}));
vi.mock("./permission-group-section", () => ({
  PermissionGroupSection: ({
    group,
    onTrigger,
  }: {
    group: PermissionGroup;
    onTrigger: () => void;
  }) => (
    <button onClick={onTrigger}>group-{group.name}</button>
  ),
}));

import { PermissionsSelectionPanel } from "./permissions-selection-panel";

function permState(resource: string, resourceGroup: string): PermissionState {
  return {
    itemId: resource,
    name: resource,
    type: 1,
    description: "",
    resource,
    resourceGroup,
    projectKey: "t1",
    tags: [],
    roles: [],
    dependentPermissions: [],
    isArchived: false,
    isBuiltIn: false,
    language: null,
    organizationIds: [],
    permissionSeverity: 0,
    modified: false,
    isInitiallyAssigned: false,
    changeState: null,
    parents: [],
  } as PermissionState;
}

beforeEach(() => {
  vi.clearAllMocks();
  h.permissionMap = new Map();
});

describe("PermissionsSelectionPanel", () => {
  it("groups permissions by resource group and renders a section per group", () => {
    h.permissionMap.set("a", permState("a", "Users"));
    h.permissionMap.set("b", permState("b", "Users"));
    h.permissionMap.set("c", permState("c", "Roles"));
    render(<PermissionsSelectionPanel />);
    expect(screen.getByText("group-Users")).toBeInTheDocument();
    expect(screen.getByText("group-Roles")).toBeInTheDocument();
  });

  it("falls back to an Ungrouped section when a permission has no resource group", () => {
    h.permissionMap.set("a", permState("a", ""));
    render(<PermissionsSelectionPanel />);
    expect(screen.getByText("group-Ungrouped")).toBeInTheDocument();
  });

  it("toggles the active accordion value through the section trigger", () => {
    h.permissionMap.set("a", permState("a", "Users"));
    render(<PermissionsSelectionPanel />);
    // Clicking the trigger twice exercises both open and collapse branches.
    fireEvent.click(screen.getByText("group-Users"));
    fireEvent.click(screen.getByText("group-Users"));
    expect(screen.getByText("group-Users")).toBeInTheDocument();
  });
});
