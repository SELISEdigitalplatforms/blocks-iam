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
vi.mock("./permission-toggle-card", () => ({
  PermissionToggleCard: ({
    onCheckedChange,
  }: {
    onCheckedChange: (c: boolean) => void;
  }) => (
    <div>
      <button onClick={() => onCheckedChange(true)}>toggle-on</button>
      <button onClick={() => onCheckedChange(false)}>toggle-off</button>
    </div>
  ),
}));
vi.mock("./required-permission-dialog", () => ({
  RequiredPermissionsDialog: ({ open }: { open: boolean }) =>
    open ? <div>required-open</div> : null,
}));
vi.mock("./affected-dependents-dialog", () => ({
  AffectedPermissionsDialog: ({ open }: { open: boolean }) =>
    open ? <div>affected-open</div> : null,
}));

import { PermissionSelectionRow } from "./permission-selection-row";

function permState(overrides: Partial<PermissionState>): PermissionState {
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

describe("PermissionSelectionRow", () => {
  it("opens the required-permissions dialog when the permission has dependents", () => {
    render(
      <PermissionSelectionRow
        permission={permState({ resource: "res", dependentPermissions: ["dep"] })}
      />,
    );
    fireEvent.click(screen.getByText("toggle-on"));
    expect(screen.getByText("required-open")).toBeInTheDocument();
    expect(h.changePermissionSelection).not.toHaveBeenCalled();
  });

  it("opens the affected-permissions dialog when unchecking a permission whose parent is checked", () => {
    h.permissionMap.set(
      "parent",
      permState({ resource: "parent", isInitiallyAssigned: true }),
    );
    render(
      <PermissionSelectionRow
        permission={permState({ resource: "child", parents: ["parent"] })}
      />,
    );
    fireEvent.click(screen.getByText("toggle-off"));
    expect(screen.getByText("affected-open")).toBeInTheDocument();
    expect(h.changePermissionSelection).not.toHaveBeenCalled();
  });

  it("applies the change directly when there are no dependents or checked parents", () => {
    render(
      <PermissionSelectionRow permission={permState({ resource: "res" })} />,
    );
    fireEvent.click(screen.getByText("toggle-on"));
    expect(h.changePermissionSelection).toHaveBeenCalledWith([
      { permissionResource: "res", isChecked: true },
    ]);
  });
});
