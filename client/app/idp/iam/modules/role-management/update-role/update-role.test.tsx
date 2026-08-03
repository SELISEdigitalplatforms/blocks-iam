import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  update: vi.fn(),
  isPending: false,
  toast: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/genesis-os", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { itemId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/iam/hooks/use-roles", () => ({
  useUpdateRole: () => ({ mutateAsync: h.update, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  useToast: () => ({ toast: h.toast }),
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { UpdateRole } from "./update-role";

const role = { itemId: "r1", name: "Admin", slug: "admin", description: "the admin" } as never;

const renderDialog = () =>
  render(
    <Dialog open onOpenChange={() => {}}>
      <UpdateRole role={role} isOpen onClose={vi.fn()} />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("UpdateRole", () => {
  it("renders the update-role form prefilled with the role", () => {
    renderDialog();
    expect(screen.getByText("Update Role")).toBeInTheDocument();
    expect((screen.getByPlaceholderText("Enter name") as HTMLInputElement).value).toBe("Admin");
  });

  it("updates the role and shows a success toast", async () => {
    h.update.mockResolvedValue({});
    renderDialog();
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "Administrator" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Update" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Update" }));
    await waitFor(() =>
      expect(h.update).toHaveBeenCalledWith(
        expect.objectContaining({ name: "Administrator", itemId: "r1", projectKey: "tenant-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the update throws", async () => {
    h.update.mockRejectedValue({ errors: { name: "taken" } });
    renderDialog();
    fireEvent.input(screen.getByPlaceholderText("Enter name"), { target: { value: "Administrator" } });
    await waitFor(() => expect(screen.getByRole("button", { name: "Update" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Update" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { name: "taken" } }));
  });
});
