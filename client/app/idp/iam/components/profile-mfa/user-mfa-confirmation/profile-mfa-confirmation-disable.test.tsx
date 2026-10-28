import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  generate: vi.fn(),
  disable: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  setOpen: vi.fn(),
}));

vi.mock("../profile-mfa", async () => {
  const React = await import("react");
  return {
    profileMfaContext: React.createContext({
      userId: "u1",
      isDisableModalOpen: true,
      setIsDisableModalOpen: h.setOpen,
    }),
  };
});
vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useGenerateUserMfaOTP: () => ({ mutateAsync: h.generate, isPending: false }),
  useDisableMfa: () => ({ mutateAsync: h.disable, isPending: false }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("@/components/ui-kits/input-otp/input-otp", () => ({
  InputOTP: ({ value, onChange }: { value: string; onChange: (v: string) => void }) => (
    <input aria-label="otp" value={value} onChange={(e) => onChange(e.target.value)} />
  ),
  InputOTPGroup: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  InputOTPSlot: () => <span />,
}));

import { UserMFAConfirmationDisable } from "./profile-mfa-confirmation-disable";

const openForm = async () => {
  h.generate.mockResolvedValue({ mfaId: "mfa-1" });
  render(<UserMFAConfirmationDisable mfaType={2} />);
  fireEvent.click(screen.getByText("Yes"));
  await screen.findByPlaceholderText("Enter your password");
};

const submit = () => {
  fireEvent.change(screen.getByPlaceholderText("Enter your password"), { target: { value: "Passw0rd!" } });
  fireEvent.change(screen.getByLabelText("otp"), { target: { value: "123456" } });
  fireEvent.click(screen.getByText("Disable MFA"));
};

beforeEach(() => vi.clearAllMocks());

describe("UserMFAConfirmationDisable", () => {
  it("asks for a code for the current method, then disables with password, mfaId and code", async () => {
    h.disable.mockResolvedValue({ isSuccess: true });
    vi.spyOn(globalThis, "location", "get").mockReturnValue({ ...globalThis.location, assign: vi.fn() });
    await openForm();
    expect(h.generate).toHaveBeenCalledWith({ userId: "u1", mfaType: 2 });
    submit();
    await waitFor(() =>
      expect(h.disable).toHaveBeenCalledWith({
        password: "Passw0rd!",
        mfaId: "mfa-1",
        verificationCode: "123456",
      }),
    );
  });

  it("shows the wrong-password message when the server answers invalid_username_password", async () => {
    h.disable.mockRejectedValue({ errors: { error: "invalid_username_password" } });
    await openForm();
    submit();
    await waitFor(() =>
      expect(h.showError).toHaveBeenCalledWith({ errors: "The password is incorrect." }),
    );
  });
});
