import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { Dialog } from "@/components/ui-kits/dialog/dialog";

const h = vi.hoisted(() => ({
  verifyOtp: vi.fn(),
  verifyTotp: vi.fn(),
  isPending: false,
  mfaMethodType: 2,
  setIsVerifyModalOpen: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("../../profile-mfa", async () => {
  const React = await import("react");
  return {
    profileMfaContext: React.createContext({
      setIsVerifyModalOpen: h.setIsVerifyModalOpen,
      mfaMethodType: h.mfaMethodType,
      userId: "u1",
    }),
  };
});
vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useVerifyMfaOTP: () => ({ mutateAsync: h.verifyOtp, isPending: h.isPending }),
  useVerifyTotpSetup: () => ({ mutateAsync: h.verifyTotp, isPending: h.isPending }),
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

import { ProfileMfaVerifyForm } from "./profile-mfa-verify-form";

const renderForm = () =>
  render(
    <Dialog open onOpenChange={() => {}}>
      <ProfileMfaVerifyForm mfaId="mfa-1" />
    </Dialog>,
  );

const enterCode = () => fireEvent.change(screen.getByLabelText("otp"), { target: { value: "12345" } });

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.mfaMethodType = 2;
});

describe("ProfileMfaVerifyForm", () => {
  it("verifies an OTP code and shows a success toast", async () => {
    h.verifyOtp.mockResolvedValue({ isSuccess: true, isValid: true });
    renderForm();
    enterCode();
    fireEvent.click(screen.getByRole("button", { name: "Verify" }));
    await waitFor(() =>
      expect(h.verifyOtp).toHaveBeenCalledWith(
        expect.objectContaining({ mfaId: "mfa-1", verificationCode: "12345", authType: 2 }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(h.setIsVerifyModalOpen).toHaveBeenCalledWith(false);
  });

  it("shows an error toast when the OTP is not valid", async () => {
    h.verifyOtp.mockResolvedValue({ isSuccess: true, isValid: false, errors: "Code is not valid" });
    renderForm();
    enterCode();
    fireEvent.click(screen.getByRole("button", { name: "Verify" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "Code is not valid" }));
  });
});
