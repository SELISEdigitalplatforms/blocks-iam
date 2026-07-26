import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  configure: vi.fn(),
  me: { data: { data: { userMfaType: 2, mfaEnabled: true, isMfaVerified: true } }, isLoading: false },
  showErrorToast: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useConfigureUserMFA: () => ({ isPending: false, mutateAsync: h.configure }),
  useGetMFAConfig: () => ({
    isLoading: false,
    isFetching: false,
    data: { enabled: true, allowedMethods: [1, 2] },
  }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetMe: () => h.me,
  useGetUserById: () => ({ data: undefined, isLoading: false, isFetching: false }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
  showSuccessToast: vi.fn(),
}));

import { ProfileMFAConfigManage } from "./profile-mfa-config-manage";
import { profileMfaContext } from "../profile-mfa";

const showVerifyModal = vi.fn();

const renderManage = () =>
  render(
    <profileMfaContext.Provider
      value={{
        projectKey: "p1",
        userId: "u1",
        own: true,
        isVerifyModalOpen: false,
        setIsVerifyModalOpen: vi.fn(),
        isDisableModalOpen: false,
        setIsDisableModalOpen: vi.fn(),
        showVerifyModal,
        mfaMethodType: 0,
      }}
    >
      <ProfileMFAConfigManage />
    </profileMfaContext.Provider>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  h.me.data.data = { userMfaType: 2, mfaEnabled: true, isMfaVerified: true };
});

describe("ProfileMFAConfigManage", () => {
  it("opens the switch dialog and lists the methods", () => {
    renderManage();
    fireEvent.click(screen.getByRole("button", { name: /Switch/i }));
    expect(screen.getByText("Switch MFA?")).toBeInTheDocument();
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("Authenticator app")).toBeInTheDocument();
  });

  it("configures a new method and opens the verify modal on save", async () => {
    h.configure.mockResolvedValue({ isSuccess: true });
    renderManage();
    fireEvent.click(screen.getByRole("button", { name: /Switch/i }));

    // Change from the current type (2) to the authenticator app (type 1).
    fireEvent.click(screen.getByText("Authenticator app"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.configure).toHaveBeenCalledWith({
        mfaEnabled: true,
        userId: "u1",
        userMfaType: 1,
      }),
    );
    expect(showVerifyModal).toHaveBeenCalledWith(1);
  });

  it("shows an error toast when the configure call reports failure", async () => {
    h.configure.mockResolvedValue({ isSuccess: false, errors: { code: "bad" } });
    renderManage();
    fireEvent.click(screen.getByRole("button", { name: /Switch/i }));
    fireEvent.click(screen.getByText("Authenticator app"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: { code: "bad" } }),
    );
    expect(showVerifyModal).not.toHaveBeenCalled();
  });

  it("verifies an already-selected but unverified method without reconfiguring", async () => {
    h.me.data.data = { userMfaType: 2, mfaEnabled: true, isMfaVerified: false };
    renderManage();
    fireEvent.click(screen.getByRole("button", { name: /Switch/i }));
    // Keep the same type (2) and save.
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(showVerifyModal).toHaveBeenCalledWith(2));
    expect(h.configure).not.toHaveBeenCalled();
  });
});
