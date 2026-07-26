import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  mutateAsync: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useChangePassword: () => ({ mutateAsync: h.mutateAsync, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
// The strength checker reports requirements met so the submit button enables.
vi.mock(
  "@blocks-idp/authentication/components/password-strength-checker/password-strength-checker",
  () => ({
    PasswordStrengthChecker: ({ onRequirementsMet }: { onRequirementsMet: (v: boolean) => void }) => {
      onRequirementsMet(true);
      return <div data-testid="strength" />;
    },
  }),
);

import { ProfileChangePassword } from "./profile-change-password";

const openDialog = () => {
  render(<ProfileChangePassword />);
  fireEvent.click(screen.getByRole("button", { name: "Update Password" }));
};

const fillForm = () => {
  fireEvent.input(screen.getByPlaceholderText("Enter your current password"), {
    target: { value: "oldPass1" },
  });
  fireEvent.input(screen.getByPlaceholderText("Enter your new password"), {
    target: { value: "newPass12" },
  });
  fireEvent.input(screen.getByPlaceholderText("Confirm your new password"), {
    target: { value: "newPass12" },
  });
};

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
});

describe("ProfileChangePassword", () => {
  it("renders the card and opens the change-password dialog", async () => {
    openDialog();
    await waitFor(() => expect(screen.getByPlaceholderText("Enter your current password")).toBeInTheDocument());
    expect(screen.getByPlaceholderText("Enter your current password")).toBeInTheDocument();
  });

  it("changes the password and shows a success toast", async () => {
    h.mutateAsync.mockResolvedValue(undefined);
    openDialog();
    await waitFor(() => expect(screen.getByPlaceholderText("Enter your current password")).toBeInTheDocument());
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Save Changes" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Save Changes" }));
    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ oldPassword: "oldPass1", newPassword: "newPass12" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the update fails", async () => {
    h.mutateAsync.mockRejectedValue({ errors: { oldPassword: "Wrong password" } });
    openDialog();
    await waitFor(() => expect(screen.getByPlaceholderText("Enter your current password")).toBeInTheDocument());
    fillForm();
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Save Changes" })).toBeEnabled(),
    );
    fireEvent.click(screen.getByRole("button", { name: "Save Changes" }));
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith(
        expect.objectContaining({ errors: "Wrong password" }),
      ),
    );
  });
});
