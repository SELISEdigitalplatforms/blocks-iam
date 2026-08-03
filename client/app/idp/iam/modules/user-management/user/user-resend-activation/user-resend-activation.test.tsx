import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountResendActivation: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  toast: (a: unknown) => h.toast(a),
}));

import { UserResendActivationMail } from "./user-resend-activation";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("UserResendActivationMail", () => {
  it("resends the activation email and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    const setOpen = vi.fn();
    render(<UserResendActivationMail userId="u1" open={true} setOpen={setOpen} />);
    fireEvent.click(screen.getByRole("button", { name: "Resend" }));
    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalledWith({ userId: "u1" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" })),
    );
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the mutation throws", async () => {
    h.mutateAsync.mockRejectedValue(new Error("boom"));
    render(<UserResendActivationMail userId="u1" open={true} setOpen={vi.fn()} />);
    fireEvent.click(screen.getByRole("button", { name: "Resend" }));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });

  it("cancels without resending", () => {
    const setOpen = vi.fn();
    render(<UserResendActivationMail userId="u1" open={true} setOpen={setOpen} />);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(setOpen).toHaveBeenCalledWith(false);
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });
});
