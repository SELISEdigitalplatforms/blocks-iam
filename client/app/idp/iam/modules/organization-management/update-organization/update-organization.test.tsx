import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { Dialog } from "@/components/ui-kits/dialog/dialog";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IOrganization } from "@blocks-idp/iam/models/organization";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/iam/hooks/use-organization", () => ({
  useUpdateOrganization: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { UpdateOrganization } from "./update-organization";

const org = { itemId: "org-1", name: "Acme", isDisabled: false } as IOrganization;

const renderModal = (onClose = vi.fn()) =>
  render(
    <Dialog open>
      <UpdateOrganization organization={org} isOpen onClose={onClose} />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("UpdateOrganization", () => {
  it("renders the rename form prefilled with the current name", () => {
    renderModal();
    expect(screen.getByText("Rename Organization")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Acme")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Save" })).toBeDisabled();
  });

  it("renames the organization and shows a success toast", async () => {
    const onClose = vi.fn();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderModal(onClose);

    fireEvent.change(screen.getByDisplayValue("Acme"), { target: { value: "Acme Corp" } });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({ itemId: "org-1", name: "Acme Corp" });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Organization renamed successfully",
    });
    expect(onClose).toHaveBeenCalled();
  });

  it("shows an error toast when the rename is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "taken" });
    renderModal();

    fireEvent.change(screen.getByDisplayValue("Acme"), { target: { value: "Dup" } });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "taken" }));
  });

  it("shows the mapped error toast when the rename throws with errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { name: "boom" } });
    renderModal();

    fireEvent.change(screen.getByDisplayValue("Acme"), { target: { value: "Dup" } });
    const save = screen.getByRole("button", { name: "Save" });
    await waitFor(() => expect(save).not.toBeDisabled());
    fireEvent.click(save);

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { name: "boom" } }),
    );
  });
});
