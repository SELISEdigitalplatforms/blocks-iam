import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { PermissionState, PermissionMap } from "./role-details-state";

const h = vi.hoisted(() => ({
  permissionMap: new Map() as PermissionMap,
  changePermissionSelection: vi.fn(),
}));

vi.mock("./role-details-state", () => ({
  useRoleDetailsStore: (selector: (s: unknown) => unknown) =>
    selector({
      permissionMap: h.permissionMap,
      changePermissionSelection: h.changePermissionSelection,
    }),
}));

import { AffectedPermissionsDialog } from "./affected-dependents-dialog";

function permState(overrides: Partial<PermissionState>): PermissionState {
  return {
    itemId: overrides.resource ?? "id",
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
    permissionSeverity: 0,
    modified: false,
    isInitiallyAssigned: false,
    changeState: null,
    parents: [],
    ...overrides,
  } as PermissionState;
}

beforeEach(() => {
  vi.clearAllMocks();
  h.permissionMap = new Map();
});

describe("AffectedPermissionsDialog", () => {
  it("lists the parent permissions that depend on the toggled permission", () => {
    h.permissionMap.set(
      "parent",
      permState({ resource: "parent", name: "Parent Permission", description: "desc" }),
    );
    render(
      <AffectedPermissionsDialog
        permission={permState({ resource: "child", parents: ["parent"] })}
        checked={false}
        open={true}
        onOpenChange={vi.fn()}
      />,
    );
    expect(screen.getByText("Review Permission Changes")).toBeInTheDocument();
    expect(screen.getByText("Parent Permission")).toBeInTheDocument();
    expect(screen.getByText("desc")).toBeInTheDocument();
  });

  it("removes the permission and closes the dialog when Save is clicked", () => {
    const onOpenChange = vi.fn();
    h.permissionMap.set("parent", permState({ resource: "parent", name: "Parent Permission" }));
    render(
      <AffectedPermissionsDialog
        permission={permState({ resource: "child", parents: ["parent"] })}
        checked={false}
        open={true}
        onOpenChange={onOpenChange}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    expect(h.changePermissionSelection).toHaveBeenCalledWith([
      { permissionResource: "child", isChecked: false },
    ]);
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("does not render dialog content when closed", () => {
    render(
      <AffectedPermissionsDialog
        permission={permState({ resource: "child", parents: [] })}
        checked={false}
        open={false}
        onOpenChange={vi.fn()}
      />,
    );
    expect(screen.queryByText("Review Permission Changes")).not.toBeInTheDocument();
  });
});
