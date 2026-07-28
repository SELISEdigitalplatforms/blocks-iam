import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountDeactivate: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { UserDeactivate } from "./user-deactivate";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("UserDeactivate", () => {
  it("deactivates the user and shows a success toast on success", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const setOpen = vi.fn();
    render(<UserDeactivate userId="u1" open={true} setOpen={setOpen} />);
    fireEvent.click(screen.getByRole("button", { name: "Deactivate" }));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ userId: "u1" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the result is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<UserDeactivate userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Deactivate" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows the mapped errors when the mutation throws a shaped error", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { userId: "bad" } });
    render(<UserDeactivate userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Deactivate" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { userId: "bad" } }));
  });

  it("shows a generic error when the mutation throws an unshaped error", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    render(<UserDeactivate userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Deactivate" }));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
  });

  it("cancels without deactivating", () => {
    const setOpen = vi.fn();
    render(<UserDeactivate userId="u1" open={true} setOpen={setOpen} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(setOpen).toHaveBeenCalledWith(false);
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });
});
