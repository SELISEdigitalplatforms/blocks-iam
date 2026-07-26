import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  permissions: [] as { resource: string; name: string }[],
  isLoading: false,
  deletePermissions: vi.fn(),
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useUserPermissions: () => ({
    permissions: h.permissions,
    isLoading: h.isLoading,
    deletePermissions: h.deletePermissions,
  }),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (a: unknown) => h.toast(a) }));
vi.mock("./add-user-permission", () => ({ AddUserPermission: () => <div data-testid="add-permission" /> }));
vi.mock("./user-permissions-list", () => ({
  UserPermissionsList: (props: Record<string, unknown>) => {
    const perms = props.permissions as { resource: string }[];
    return (
      <div>
        {perms.map((p) => (
          <button key={p.resource} onClick={() => (props.onRemovePermission as (r: string) => void)(p.resource)}>
            remove-{p.resource}
          </button>
        ))}
      </div>
    );
  },
}));

import { UserPermissions } from "./user-permissions";

beforeEach(() => {
  vi.clearAllMocks();
  h.isLoading = false;
  h.permissions = [
    { resource: "users:read", name: "Read" },
    { resource: "users:write", name: "Write" },
  ];
});

describe("UserPermissions", () => {
  it("renders the permissions card and add control", () => {
    render(<UserPermissions userId="u1" projectKey="p1" />);
    expect(screen.getByText("Permissions")).toBeInTheDocument();
    expect(screen.getByTestId("add-permission")).toBeInTheDocument();
  });

  it("reveals Save/Reset after removing a permission", () => {
    render(<UserPermissions userId="u1" projectKey="p1" />);
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
    fireEvent.click(screen.getByText("remove-users:read"));
    expect(screen.getByRole("button", { name: "Save" })).toBeInTheDocument();
  });

  it("saves removed permissions and shows a success toast", async () => {
    h.deletePermissions.mockResolvedValue({ isSuccess: true });
    render(<UserPermissions userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("remove-users:read"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.deletePermissions).toHaveBeenCalledWith(["users:read"]));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(
        expect.objectContaining({ description: "Permissions updated successfully" }),
      ),
    );
  });

  it("shows an error toast when the save is not successful", async () => {
    h.deletePermissions.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<UserPermissions userId="u1" projectKey="p1" />);
    fireEvent.click(screen.getByText("remove-users:write"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });
});
