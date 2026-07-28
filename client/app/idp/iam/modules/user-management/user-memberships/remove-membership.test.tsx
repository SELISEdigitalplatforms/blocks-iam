import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { IMembership } from "@blocks-idp/iam/models/user";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useRevokeAccess: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { RemoveMembership } from "./remove-membership";

const membership = { organizationId: "org1" } as IMembership;

const renderModal = (onSuccess = vi.fn(), onOpenChange = vi.fn()) => {
  render(
    <RemoveMembership
      open={true}
      onOpenChange={onOpenChange}
      membership={membership}
      organizationName="Acme"
      userId="u1"
      projectKey="p1"
      onSuccess={onSuccess}
    />,
  );
  return { onSuccess, onOpenChange };
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("RemoveMembership", () => {
  it("removes the membership and notifies success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const { onSuccess, onOpenChange } = renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ organizationId: "org1" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(onSuccess).toHaveBeenCalled();
  });

  it("shows an error toast when the result is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows a generic error when the mutation throws an unshaped error", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Remove" }));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("closes without removing on cancel", () => {
    const { onOpenChange } = renderModal();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });
});
