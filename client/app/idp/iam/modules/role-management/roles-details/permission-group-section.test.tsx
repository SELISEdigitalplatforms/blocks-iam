import { render, screen, fireEvent } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Accordion } from "@/components/ui-kits/accordion/accordion";
import type { PermissionState, PermissionMap, PermissionGroup } from "./role-details-state";

const h = vi.hoisted(() => ({
  permissionMap: new Map() as PermissionMap,
  isEditMode: true,
  changePermissionGroupSelection: vi.fn(),
}));

vi.mock("./role-details-state", () => ({
  useRoleDetailsStore: (selector: (s: unknown) => unknown) =>
    selector({
      permissionMap: h.permissionMap,
      isEditMode: h.isEditMode,
      changePermissionGroupSelection: h.changePermissionGroupSelection,
    }),
}));
vi.mock("./permission-selection-row", () => ({
  PermissionSelectionRow: ({ permission }: { permission: PermissionState }) => (
    <li>row-{permission.resource}</li>
  ),
}));

import { PermissionGroupSection } from "./permission-group-section";

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

const renderSection = (group: PermissionGroup, onTrigger = vi.fn()) => {
  render(
    <Accordion type="single" collapsible>
      <PermissionGroupSection group={group} onTrigger={onTrigger} />
    </Accordion>,
  );
  return onTrigger;
};

beforeEach(() => {
  vi.clearAllMocks();
  h.permissionMap = new Map();
  h.isEditMode = true;
});

describe("PermissionGroupSection", () => {
  const group: PermissionGroup = {
    name: "Users",
    permissions: [
      permState({ resource: "a" }),
      permState({ resource: "b" }),
    ],
  };

  it("shows the group name, total count and zero selected when nothing is checked", () => {
    renderSection(group);
    expect(screen.getByText("Users")).toBeInTheDocument();
    expect(screen.getByText("2 total permissions")).toBeInTheDocument();
    expect(screen.getByText("0 selected")).toBeInTheDocument();
  });

  it("shows the missing-dependency alert when a checked permission lacks its dependents", () => {
    h.permissionMap.set(
      "a",
      permState({ resource: "a", isInitiallyAssigned: true, dependentPermissions: ["z"] }),
    );
    renderSection(group);
    expect(screen.getByText("1 selected")).toBeInTheDocument();
  });

  it("invokes onTrigger when the header is clicked", () => {
    const onTrigger = renderSection(group);
    fireEvent.click(screen.getByText("Users"));
    expect(onTrigger).toHaveBeenCalled();
  });

  it("selects the whole group when the group checkbox is toggled", () => {
    renderSection(group);
    fireEvent.click(document.getElementById("group-Users") as HTMLElement);
    expect(h.changePermissionGroupSelection).toHaveBeenCalledWith(group.permissions, true);
  });

  it("disables the group checkbox when not in edit mode", () => {
    h.isEditMode = false;
    renderSection(group);
    expect(document.getElementById("group-Users")).toBeDisabled();
  });
});
