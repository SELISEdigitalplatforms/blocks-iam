import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  user: { data: { data: { email: "user@x.com" } } as unknown },
  mutateAsync: vi.fn(),
  isPending: false,
  showSuccessToast: vi.fn(),
  showErrorToast: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({
  showSuccessToast: h.showSuccessToast,
  showErrorToast: h.showErrorToast,
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => h.user,
}));
vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useAccountRecover: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));

import { UserResetPassword } from "./user-reset-password";

const renderReset = (setOpen = vi.fn()) =>
  render(<UserResetPassword userId="u1" projectKey="t1" open setOpen={setOpen} />);

const confirm = () => fireEvent.click(screen.getByRole("button", { name: "Reset" }));

beforeEach(() => {
  vi.clearAllMocks();
  h.user = { data: { data: { email: "user@x.com" } } };
  h.isPending = false;
});

describe("UserResetPassword", () => {
  it("renders the confirmation prompt", () => {
    renderReset();
    expect(screen.getByText("Reset password")).toBeInTheDocument();
  });

  it("sends the reset email and shows a success toast", async () => {
    const setOpen = vi.fn();
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    renderReset(setOpen);

    confirm();

    await waitFor(() => expect(h.mutateAsync).toHaveBeenCalled());
    expect(h.mutateAsync.mock.calls[0][0]).toMatchObject({ email: "user@x.com", tenantId: "t1" });
    expect(h.showSuccessToast).toHaveBeenCalled();
    expect(setOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when recovery is unsuccessful", async () => {
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: "bad" });
    renderReset();

    confirm();
    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "bad" }));
  });

  it("shows a generic error toast when no email is available", async () => {
    h.user = { data: { data: { email: "" } } };
    renderReset();

    confirm();
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "Something went wrong" }),
    );
    expect(h.mutateAsync).not.toHaveBeenCalled();
  });

  it("shows the mapped error toast when recovery throws with errors", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { email: "invalid" } });
    renderReset();

    confirm();
    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { email: "invalid" } }),
    );
  });
});
