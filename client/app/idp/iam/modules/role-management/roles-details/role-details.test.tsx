import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

type StoreState = {
  role: { slug: string; itemId: string; name: string } | undefined;
  isEditMode: boolean;
  discardChanges: ReturnType<typeof vi.fn>;
  changeEditMode: ReturnType<typeof vi.fn>;
  isInitialized: boolean;
  permissionMap: Map<string, unknown>;
};

const h = vi.hoisted(() => {
  const state = {
    role: { slug: "admin", itemId: "r1", name: "Administrator" },
    isEditMode: false,
    discardChanges: vi.fn(),
    changeEditMode: vi.fn(),
    isInitialized: true,
    permissionMap: new Map(),
  };
  return {
    state,
    setRolesMutate: vi.fn(),
    showSuccessToast: vi.fn(),
    showErrorToast: vi.fn(),
  };
});

vi.mock("./role-details-state", () => ({
  RoleDetailsProvider: ({ children }: { children: React.ReactNode }) => <>{children}</>,
  useRoleDetailsStore: <T,>(selector: (s: StoreState) => T): T =>
    selector(h.state as unknown as StoreState),
}));
vi.mock("./permissions-selection-panel", () => ({
  PermissionsSelectionPanel: () => <div>permissions-panel</div>,
}));
vi.mock("@blocks-idp/iam/components/permission-severity/permission-severity", () => ({
  PermissionSeverity: () => <div>permission-severity</div>,
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({
  default: () => <div>breadcrumb</div>,
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useSetRoles: () => ({ isPending: false, mutateAsync: h.setRolesMutate }),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({
    selectedProject: { tenantId: "t1", itemId: "p1" },
    selectedTenantGroup: "tg1",
  })),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));

import { RoleDetailsContainer } from "./role-details";

const renderContainer = () =>
  render(<RoleDetailsContainer />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  h.state.role = { slug: "admin", itemId: "r1", name: "Administrator" };
  h.state.isEditMode = false;
  h.state.isInitialized = true;
  h.state.permissionMap = new Map();
});

describe("RoleDetailsContainer", () => {
  it("renders the role name and the Edit Permissions action when not editing", () => {
    renderContainer();
    expect(screen.getByText("Administrator")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /edit permissions/i })).toBeInTheDocument();
  });

  it("enters edit mode when Edit Permissions is clicked", () => {
    renderContainer();
    fireEvent.click(screen.getByRole("button", { name: /edit permissions/i }));
    expect(h.state.changeEditMode).toHaveBeenCalledWith(true);
  });

  it("shows Discard/Save actions in edit mode", () => {
    h.state.isEditMode = true;
    renderContainer();
    expect(screen.getByRole("button", { name: /discard/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /save changes/i })).toBeInTheDocument();
  });

  it("saves the added/removed permission diff and toasts on success", async () => {
    h.state.isEditMode = true;
    h.state.permissionMap = new Map<string, unknown>([
      ["a", { itemId: "a", modified: true, changeState: "added", permissionSeverity: 1 }],
      ["b", { itemId: "b", modified: true, changeState: "removed", permissionSeverity: 2 }],
      ["c", { itemId: "c", modified: false, changeState: "none", permissionSeverity: 3 }],
    ]);
    h.setRolesMutate.mockResolvedValue({ isSuccess: true });

    renderContainer();
    fireEvent.click(screen.getByRole("button", { name: /save changes/i }));

    await waitFor(() =>
      expect(h.setRolesMutate).toHaveBeenCalledWith({
        addPermissions: ["a"],
        removePermissions: ["b"],
        projectKey: "t1",
        slug: "admin",
      }),
    );
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Role permissions updated successfully",
    });
  });
});
