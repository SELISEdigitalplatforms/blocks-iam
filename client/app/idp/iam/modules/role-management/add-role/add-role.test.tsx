import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", () => ({
  useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useAddRole: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { AddRole } from "./add-role";

const openAndFill = async () => {
  fireEvent.click(screen.getByText("Add Role"));
  await waitFor(() => expect(screen.getByPlaceholderText("Enter name")).toBeInTheDocument());
  fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "Manager" } });
  fireEvent.input(screen.getByPlaceholderText("Enter slug"), { target: { value: "manager" } });
  fireEvent.input(screen.getByPlaceholderText("Enter description"), { target: { value: "mgr role" } });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("AddRole", () => {
  it("opens the dialog with the role fields", async () => {
    render(<AddRole />);
    fireEvent.click(screen.getByText("Add Role"));
    await waitFor(() => expect(screen.getByText("Please fill in the details to add a new role.")).toBeInTheDocument());
  });

  it("adds a role and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({});
    render(<AddRole />);
    await openAndFill();
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Manager", slug: "manager", projectKey: "tenant-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows a forbidden error toast on a 403 response", async () => {
    h.mutateAsync.mockRejectedValue({ status: 403, errors: {} });
    render(<AddRole />);
    await openAndFill();
    fireEvent.click(screen.getByRole("button", { name: "Add" }));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith(
        expect.objectContaining({ title: "Forbidden" }),
      ),
    );
  });
});
