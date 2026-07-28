import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IRole } from "@blocks-idp/iam/models/role";

const h = vi.hoisted(() => ({
  deleteRoles: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useUserRoles: () => ({ deleteRoles: h.deleteRoles, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { DeleteUserRole } from "./delete-user-role";

const role = { slug: "admin", name: "Admin" } as IRole;

const openDialog = () => {
  fireEvent.click(document.querySelector('[aria-haspopup="dialog"]') as HTMLElement);
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("DeleteUserRole", () => {
  it("excludes the role and shows a success toast", async () => {
    h.deleteRoles.mockResolvedValue({ isSuccess: true });
    render(<DeleteUserRole role={role} userId="u1" projectKey="p1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.deleteRoles).toHaveBeenCalledWith(["admin"]));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the result is not successful", async () => {
    h.deleteRoles.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<DeleteUserRole role={role} userId="u1" projectKey="p1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows a generic error when the mutation throws an unshaped error", async () => {
    h.deleteRoles.mockRejectedValue(new Error("boom"));
    render(<DeleteUserRole role={role} userId="u1" projectKey="p1" />);
    openDialog();
    fireEvent.click(await screen.findByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });
});
