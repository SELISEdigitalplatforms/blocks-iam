import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

/*
 * The profile dialog with the real inline PasswordStrengthChecker, which since blocks-iam#682
 * reports through onRequirementsMet only when its result changes (H3). Save must still follow
 * every change of the result (H6).
 */

vi.mock("@blocks-idp/iam/hooks/use-account", () => ({
  useChangePassword: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));
vi.mock("@blocks-idp/authentication/hooks/use-oidc-ui-config", () => ({
  useOidcUiConfig: () => ({ passwordPolicy: null, isLoading: false }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

import { ProfileChangePassword } from "./profile-change-password";

// Meets the front-end default policy that applies without a tenant policy.
const NEW_PASSWORD = "NewPass12!";
const OLD_PASSWORD = "OldPass12!";

const type = (placeholder: string, value: string) =>
  fireEvent.input(screen.getByPlaceholderText(placeholder), { target: { value } });

const save = () => screen.getByRole("button", { name: "Save Changes" });

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ProfileChangePassword with the real strength checker", () => {
  it("enables and disables Save as the requirements result changes", async () => {
    render(<ProfileChangePassword />);
    fireEvent.click(screen.getByRole("button", { name: "Update Password" }));
    await screen.findByPlaceholderText("Enter your current password");

    type("Enter your current password", OLD_PASSWORD);
    type("Enter your new password", NEW_PASSWORD);
    type("Confirm your new password", NEW_PASSWORD);
    await waitFor(() => expect(save()).toBeEnabled());

    type("Confirm your new password", `${NEW_PASSWORD}x`);
    await waitFor(() => expect(save()).toBeDisabled());

    type("Confirm your new password", NEW_PASSWORD);
    await waitFor(() => expect(save()).toBeEnabled());

    // The same as the current password fails the exclusion row.
    type("Enter your new password", OLD_PASSWORD);
    type("Confirm your new password", OLD_PASSWORD);
    await waitFor(() => expect(save()).toBeDisabled());
  });
});
