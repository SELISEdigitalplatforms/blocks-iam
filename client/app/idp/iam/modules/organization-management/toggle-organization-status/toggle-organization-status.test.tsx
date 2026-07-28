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

import { ToggleOrganizationStatus } from "./toggle-organization-status";

const org = { itemId: "org-1", name: "Acme", isDisabled: false } as IOrganization;

const renderToggle = (o: Partial<IOrganization> = {}, onClose = vi.fn()) =>
  render(
    <Dialog open>
      <ToggleOrganizationStatus organization={{ ...org, ...o }} onClose={onClose} />
    </Dialog>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("ToggleOrganizationStatus", () => {
  it("shows the Disable prompt for an active organization", () => {
    renderToggle();
    expect(screen.getByText("Disable Organization")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Disable" })).toBeInTheDocument();
  });

  it("shows the Enable prompt for a disabled organization", () => {
    renderToggle({ isDisabled: true });
    expect(screen.getByText("Enable Organization")).toBeInTheDocument();
  });

  it("disables the organization and shows a success toast", async () => {
    const onClose = vi.fn();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderToggle({}, onClose);

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({ itemId: "org-1", isEnable: false });
    expect(h.showSuccessToast).toHaveBeenCalledWith({
      description: "Organization disabled successfully",
    });
    expect(onClose).toHaveBeenCalled();
  });

  it("shows an error toast when the update is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderToggle();

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows the mapped error toast when the update throws with errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { org: "boom" } });
    renderToggle();

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { org: "boom" } }),
    );
  });
});
