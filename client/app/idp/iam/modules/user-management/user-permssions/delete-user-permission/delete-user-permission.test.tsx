import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IPermission } from "@blocks-idp/iam/models/permission";

const h = vi.hoisted(() => ({
  deletePermissions: vi.fn(),
  isPending: false,
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useUserPermissions: () => ({ deletePermissions: h.deletePermissions, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  toast: (a: unknown) => h.toast(a),
}));
vi.mock("@/store/useProjectStore", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "t1" } }),
}));

import { DeleteUserPermission } from "./delete-user-permission";

const permission = { resource: "user:read", name: "Read Users" } as IPermission;

const openDialog = () => {
  fireEvent.click(document.querySelector('[aria-haspopup="dialog"]') as HTMLElement);
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("DeleteUserPermission", () => {
  it("excludes the permission and shows a success toast", async () => {
    h.deletePermissions.mockResolvedValue({ isSuccess: true });
    render(<DeleteUserPermission permission={permission} userId="u1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.deletePermissions).toHaveBeenCalledWith(["user:read"]));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" })),
    );
  });

  it("shows an error toast when the result is not successful", async () => {
    h.deletePermissions.mockResolvedValue({ isSuccess: false });
    render(<DeleteUserPermission permission={permission} userId="u1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });

  it("shows an error toast when the mutation throws", async () => {
    h.deletePermissions.mockRejectedValue(new Error("boom"));
    render(<DeleteUserPermission permission={permission} userId="u1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });
});
