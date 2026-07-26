import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  perm: { data: undefined as unknown, isLoading: false },
  mutateAsync: vi.fn(),
  isPending: false,
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: vi.fn(() => ({ selectedProject: { tenantId: "t1" } })),
}));
vi.mock("@blocks-idp/iam/hooks/use-permission", () => ({
  useGetPermissionById: () => h.perm,
  useUpdatePermission: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@/components/breadcrumb/breadcrumb", () => ({ default: () => <nav /> }));
vi.mock("./permission-roles-list", () => ({ PermissionRolesList: () => <div data-testid="roles-list" /> }));
vi.mock("../permission-form", () => ({
  PermissionForm: ({ onSave }: { onSave: (d: unknown) => void }) => (
    <button onClick={() => onSave({ name: "Read", resource: "users:read", type: "1", dependentPermissions: [] })}>
      save-permission
    </button>
  ),
}));

import { PermissionDetails } from "./permission-details";

beforeEach(() => {
  vi.clearAllMocks();
  h.perm = {
    data: { data: { name: "Read Users", isBuiltIn: false, roles: [] } },
    isLoading: false,
  };
  h.isPending = false;
});

describe("PermissionDetails", () => {
  it("shows the loading skeleton while fetching", () => {
    h.perm = { data: undefined, isLoading: true };
    const { container } = render(<PermissionDetails id="perm-1" />);
    expect(container.querySelectorAll("[class*='rounded']").length).toBeGreaterThan(0);
    expect(screen.queryByText("save-permission")).toBeNull();
  });

  it("renders the permission name and a custom badge", () => {
    render(<PermissionDetails id="perm-1" />);
    expect(screen.getByText("Read Users")).toBeInTheDocument();
    expect(screen.getByText("Custom")).toBeInTheDocument();
  });

  it("renders the Built In badge for built-in permissions", () => {
    h.perm = { data: { data: { name: "Admin", isBuiltIn: true, roles: [] } }, isLoading: false };
    render(<PermissionDetails id="perm-1" />);
    expect(screen.getByText("Built In")).toBeInTheDocument();
  });

  it("updates the permission and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<PermissionDetails id="perm-1" />);

    fireEvent.click(screen.getByText("save-permission"));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({ itemId: "perm-1", type: 1 });
    expect(h.showSuccessToast).toHaveBeenCalledWith({ description: "Permission Updated successfully" });
  });

  it("shows an error toast when the update is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "dup" });
    render(<PermissionDetails id="perm-1" />);

    fireEvent.click(screen.getByText("save-permission"));
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "dup" }));
  });

  it("shows a generic error toast when the update throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("network"));
    render(<PermissionDetails id="perm-1" />);

    fireEvent.click(screen.getByText("save-permission"));
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
