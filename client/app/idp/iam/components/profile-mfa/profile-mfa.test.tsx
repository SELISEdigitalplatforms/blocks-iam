import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

// input-otp schedules a timeout that calls document.elementFromPoint, which
// jsdom does not implement. Polyfill it so the OTP field in the verify form
// does not raise an unhandled error after the test completes.
if (typeof document.elementFromPoint !== "function") {
  document.elementFromPoint = () => null;
}

const h = vi.hoisted(() => ({
  mfaConfig: { isLoading: false, isFetching: false, data: { enabled: true, allowedMethods: [1, 2] } },
  me: {
    data: { data: { userMfaType: 0, mfaEnabled: false, isMfaVerified: false, email: "a@b.com" } },
    isLoading: false,
  },
  generateOtp: vi.fn().mockResolvedValue({ mfaId: "m1" }),
  verifyOtp: vi.fn(),
  verifyTotp: vi.fn(),
  disable: vi.fn(),
  resend: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useGetMFAConfig: () => h.mfaConfig,
  useGenerateUserMfaOTP: () => ({ mutateAsync: h.generateOtp }),
  useVerifyMfaOTP: () => ({ mutateAsync: h.verifyOtp, isPending: false }),
  useVerifyTotpSetup: () => ({ mutateAsync: h.verifyTotp, isPending: false }),
  useGetTotp: () => ({ data: { qrImageUrl: "qr.png", secret: "SECRET" } }),
  useDisableMfa: () => ({ isPending: false, mutateAsync: h.disable }),
  useConfigureUserMFA: () => ({ isPending: false, mutateAsync: vi.fn() }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.me,
  useGetUserById: () => ({ data: undefined, isLoading: false, isFetching: false }),
}));
vi.mock("@blocks-idp/mfa/hooks/use-resend-otp", () => ({
  useResendOtp: () => ({ remainingTime: 0, resend: h.resend }),
}));

import { ProfileMFA } from "./profile-mfa";

const renderMFA = () =>
  render(<ProfileMFA userId="u1" projectKey="p1" own />, { wrapper: createWrapper() });

beforeEach(() => {
  vi.clearAllMocks();
  h.mfaConfig.isLoading = false;
  h.mfaConfig.data = { enabled: true, allowedMethods: [1, 2] };
  h.me.data.data = {
    userMfaType: 0,
    mfaEnabled: false,
    isMfaVerified: false,
    email: "a@b.com",
  };
});

describe("ProfileMFA", () => {
  it("renders the loading skeleton while the config loads", () => {
    h.mfaConfig.isLoading = true;
    const { container } = renderMFA();
    expect(container.querySelector(".animate-pulse")).not.toBeNull();
  });

  it("renders the project prompt when MFA is not enabled for the project", () => {
    h.mfaConfig.data = { enabled: false, allowedMethods: [] };
    renderMFA();
    expect(screen.getByText("Go to MFA Settings")).toBeInTheDocument();
  });

  it("lists the available methods with enable actions when MFA is off", () => {
    renderMFA();
    expect(screen.getByText("Multi-factor Authentication")).toBeInTheDocument();
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("Authenticator app")).toBeInTheDocument();
    expect(screen.getAllByText("Enable").length).toBeGreaterThanOrEqual(2);
  });

  it("opens the email verification flow when enabling the email method", async () => {
    renderMFA();
    fireEvent.click(screen.getAllByText("Enable")[0]);
    await waitFor(() => expect(h.generateOtp).toHaveBeenCalledWith({ userId: "u1", mfaType: 2 }));
    expect(await screen.findByText("Email sent")).toBeInTheDocument();
  });

  it("opens the authenticator (TOTP) verification flow with a QR guideline", async () => {
    renderMFA();
    fireEvent.click(screen.getAllByText("Enable")[1]);
    await waitFor(() =>
      expect(screen.getByText("Set up your authenticator app")).toBeInTheDocument(),
    );
    expect(screen.getByAltText("qr_code")).toBeInTheDocument();
  });

  it("shows the disable confirmation when MFA is already enabled", () => {
    h.me.data.data = {
      userMfaType: 2,
      mfaEnabled: true,
      isMfaVerified: true,
      email: "a@b.com",
    };
    renderMFA();
    expect(screen.getAllByText("Active").length).toBeGreaterThan(0);
    fireEvent.click(screen.getAllByText("Disable")[0]);
    expect(screen.getByText("Disable MFA?")).toBeInTheDocument();
  });
});
