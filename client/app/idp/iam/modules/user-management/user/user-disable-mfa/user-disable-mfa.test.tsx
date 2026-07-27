import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useDisableMfa: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { UserDisableMFA } from "./user-disable-mfa";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("UserDisableMFA", () => {
  it("disables MFA and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const setOpen = vi.fn();
    render(<UserDisableMFA userId="u1" open={true} setOpen={setOpen} />);
    fireEvent.click(screen.getByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ userId: "u1" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the result is not successful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<UserDisableMFA userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });

  it("shows the mapped errors when the mutation throws a shaped error", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { userId: "bad" } });
    render(<UserDisableMFA userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Yes" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: { userId: "bad" } }));
  });

  it("shows the processing label while the mutation is pending", () => {
    h.isPending = true;
    render(<UserDisableMFA userId="u1" open={true} setOpen={vi.fn()} />);
    expect(screen.getByRole("button", { name: "Processing" })).toBeDisabled();
  });
});
