import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

// input-otp schedules a timeout that calls document.elementFromPoint, absent in jsdom.
if (typeof document.elementFromPoint !== "function") {
  document.elementFromPoint = () => null;
}

const h = vi.hoisted(() => ({
  methodConfig: { isLoading: false, isFetching: false, data: { enabled: true, allowedMethods: [1, 2] } },
  user: {
    data: { data: { mfaEnabled: true, userMfaType: 2, isVarified: true, active: true, email: "a@b.com" } },
    isLoading: false,
    isFetching: false,
  },
  configure: vi.fn(),
  generateOtp: vi.fn().mockResolvedValue({ isSuccess: true, mfaId: "m1" }),
  verifyOtp: vi.fn(),
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => h.methodConfig,
  useConfigureUserMFA: () => ({ isPending: false, mutateAsync: h.configure }),
  useGenerateUserMfaOTP: () => ({ mutateAsync: h.generateOtp }),
  useVerifyMfaOTP: () => ({ mutateAsync: h.verifyOtp, isPending: false }),
  useGetTotp: () => ({ data: { qrImageUrl: "qr.png", secret: "SECRET" } }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => h.user,
  useGetMe: () => ({ data: undefined, isLoading: false }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
  toast: h.toast,
}));

import { UserMFA, userMfaContext } from "./user-mfa";
import { UserMFAConfirmationEnable } from "./user-mfa-confirmation/user-mfa-confirmation-enable";
import { UserMFAConfigManage } from "./user-mfa-confirmation/user-mfa-config-manage";
import { UserMFAVerify } from "./user-mfa-confirmation/user-mfa-veriffy/user-mfa-verify";

const ctxValue = (overrides: Record<string, unknown> = {}) => ({
  projectKey: "p1",
  userId: "u1",
  enableTotpModal: false,
  isTotpModalOpen: false,
  setIsTotpModalOpen: vi.fn(),
  showTotpModal: vi.fn(),
  mfaMethodType: 2,
  ...overrides,
});

const withCtx = (node: React.ReactNode, overrides = {}) => (
  <userMfaContext.Provider value={ctxValue(overrides)}>{node}</userMfaContext.Provider>
);

beforeEach(() => {
  vi.clearAllMocks();
  h.methodConfig.isLoading = false;
  h.methodConfig.data = { enabled: true, allowedMethods: [1, 2] };
  h.user.data.data = {
    mfaEnabled: true,
    userMfaType: 2,
    isVarified: true,
    active: true,
    email: "a@b.com",
  };
});

describe("UserMFA composed with real children", () => {
  it("renders the enabled detail and drives the disable dialog", async () => {
    h.configure.mockResolvedValue({ isSuccess: true });
    render(<UserMFA userId="u1" projectKey="p1" />);
    expect(screen.getByText(/MFA\) is enabled on your account/)).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Disable" }));
    expect(screen.getByText("Disable MFA?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Yes" }));
    await waitFor(() =>
      expect(h.configure).toHaveBeenCalledWith({ mfaEnabled: false, userId: "u1", userMfaType: 0 }),
    );
  });

  it("shows the disabled detail message when the user has MFA off", () => {
    h.user.data.data = { ...h.user.data.data, mfaEnabled: false };
    render(<UserMFA userId="u1" projectKey="p1" />);
    expect(screen.getByText(/currently disabled for this user/)).toBeInTheDocument();
  });
});

describe("UserMFAConfirmationEnable", () => {
  it("warns when the user is not verified", () => {
    h.user.data.data = { ...h.user.data.data, isVarified: false };
    render(withCtx(<UserMFAConfirmationEnable />));
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ description: "Please verify the user first" }),
    );
  });

  it("warns when the user is not active", () => {
    h.user.data.data = { ...h.user.data.data, isVarified: true, active: false };
    render(withCtx(<UserMFAConfirmationEnable />));
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ description: "Please active the user first" }),
    );
  });

  it("enables MFA with the selected method", async () => {
    h.configure.mockResolvedValue({ isSuccess: true });
    render(withCtx(<UserMFAConfirmationEnable />, { enableTotpModal: true }));
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    expect(screen.getByText("Enable MFA?")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Authenticator app"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.configure).toHaveBeenCalledWith({ mfaEnabled: true, userId: "u1", userMfaType: 1 }),
    );
  });
});

describe("UserMFAConfigManage", () => {
  it("saves a changed method", async () => {
    h.configure.mockResolvedValue({ isSuccess: true });
    render(withCtx(<UserMFAConfigManage />));
    fireEvent.click(screen.getByText("Authenticator app"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.configure).toHaveBeenCalledWith({ mfaEnabled: true, userId: "u1", userMfaType: 1 }),
    );
  });
});

describe("UserMFAVerify", () => {
  it("generates an OTP and shows the email verify form", async () => {
    render(withCtx(<UserMFAVerify />, { isTotpModalOpen: true, mfaMethodType: 2 }));
    await waitFor(() => expect(h.generateOtp).toHaveBeenCalledWith({ userId: "u1", mfaType: 2 }));
    expect(screen.getByRole("button", { name: "Verify" })).toBeInTheDocument();
  });

  it("shows the TOTP setup guideline for the authenticator method", async () => {
    render(withCtx(<UserMFAVerify />, { isTotpModalOpen: true, mfaMethodType: 1 }));
    await waitFor(() =>
      expect(screen.getByText("Set up your authenticator app")).toBeInTheDocument(),
    );
  });
});
