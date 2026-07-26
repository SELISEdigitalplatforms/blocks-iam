import { render, screen, act } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  roleResult: {} as Record<string, unknown>,
  permsData: undefined as unknown,
  getPermissions: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useGetRoleById: () => h.roleResult,
}));
vi.mock("@blocks-idp/iam/services/permission.service", () => ({
  permissionService: { getPermissions: (...args: unknown[]) => h.getPermissions(...args) },
}));
vi.mock("@tanstack/react-query", () => ({
  useQuery: () => ({ data: h.permsData }),
}));

import {
  RoleDetailsProvider,
  useRoleDetailsStore,
  type PermissionState,
} from "./role-details-state";

const perm = (over: Record<string, unknown>) =>
  ({
    itemId: over.resource,
    name: over.resource,
    resource: over.resource,
    roles: over.roles ?? [],
    dependentPermissions: over.dependentPermissions ?? [],
    ...over,
  }) as unknown;

let api: {
  map: Map<string, PermissionState>;
  isEditMode: boolean;
  isInitialized: boolean;
  changePermissionSelection: (c: { permissionResource: string; isChecked: boolean }[]) => void;
  changeEditMode: (v: boolean) => void;
  discardChanges: () => void;
};

const Probe = () => {
  const map = useRoleDetailsStore((s) => s.permissionMap);
  const isEditMode = useRoleDetailsStore((s) => s.isEditMode);
  const isInitialized = useRoleDetailsStore((s) => s.isInitialized);
  const changePermissionSelection = useRoleDetailsStore((s) => s.changePermissionSelection);
  const changeEditMode = useRoleDetailsStore((s) => s.changeEditMode);
  const discardChanges = useRoleDetailsStore((s) => s.discardChanges);
  api = { map, isEditMode, isInitialized, changePermissionSelection, changeEditMode, discardChanges };
  return <div data-testid="count">{map.size}</div>;
};

const renderProvider = () =>
  render(
    <RoleDetailsProvider id="r1" projectKey="p1">
      <Probe />
    </RoleDetailsProvider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.roleResult = { data: { data: { slug: "admin" } } };
  h.permsData = {
    data: [
      perm({ resource: "users:read", roles: ["admin"], dependentPermissions: ["users:edit"] }),
      perm({ resource: "users:edit", roles: [] }),
    ],
  };
});

describe("role-details-state store", () => {
  it("throws when used outside its provider", () => {
    const Bad = () => {
      useRoleDetailsStore((s) => s.isInitialized);
      return null;
    };
    expect(() => render(<Bad />)).toThrow("Missing RoleDetailsProvider");
  });

  it("initializes the permission map from the fetched permissions and role", () => {
    renderProvider();
    expect(screen.getByTestId("count")).toHaveTextContent("2");
    expect(api.isInitialized).toBe(true);
    const read = api.map.get("users:read")!;
    expect(read.isInitiallyAssigned).toBe(true);
    const edit = api.map.get("users:edit")!;
    expect(edit.parents).toContain("users:read");
  });

  it("marks a permission as added when newly checked", () => {
    renderProvider();
    act(() => api.changePermissionSelection([{ permissionResource: "users:edit", isChecked: true }]));
    expect(api.map.get("users:edit")!.changeState).toBe("added");
    expect(api.map.get("users:edit")!.modified).toBe(true);
  });

  it("marks an initially assigned permission as removed when unchecked", () => {
    renderProvider();
    act(() => api.changePermissionSelection([{ permissionResource: "users:read", isChecked: false }]));
    expect(api.map.get("users:read")!.changeState).toBe("removed");
  });

  it("toggles edit mode and discards pending changes", () => {
    renderProvider();
    act(() => api.changeEditMode(true));
    expect(api.isEditMode).toBe(true);
    act(() => api.changePermissionSelection([{ permissionResource: "users:edit", isChecked: true }]));
    act(() => api.discardChanges());
    expect(api.isEditMode).toBe(false);
    expect(api.map.get("users:edit")!.changeState).toBeNull();
    expect(api.map.get("users:edit")!.modified).toBe(false);
  });
});
