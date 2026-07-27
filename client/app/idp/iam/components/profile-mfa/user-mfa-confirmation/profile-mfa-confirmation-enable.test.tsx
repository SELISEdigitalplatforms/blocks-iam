import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  configure: vi.fn(),
  isPending: false,
  userById: {} as Record<string, unknown>,
  me: {} as Record<string, unknown>,
  showVerify: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
  toast: vi.fn(),
}));

vi.mock("@blocks-idp/mfa/hooks/use-mfa-config", () => ({
  useConfigureUserMFA: () => ({ isPending: h.isPending, mutateAsync: h.configure }),
}));
vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetUserById: () => h.userById,
  useGetMe: () => h.me,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
  toast: (a: unknown) => h.toast(a),
}));
vi.mock("./profile-mfa-methods-list", () => ({
  ProfileMFAMethodList: ({ setSelected }: { setSelected: (v: number) => void }) => (
    <button onClick={() => setSelected(2)}>select-method</button>
  ),
}));
vi.mock("../profile-mfa", async () => {
  const React = await import("react");
  return {
    profileMfaContext: React.createContext({
      projectKey: "p1",
      userId: "u1",
      own: false,
      showVerifyModal: h.showVerify,
    }),
  };
});

import { UserMFAConfirmationEnable } from "./profile-mfa-confirmation-enable";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.userById = { data: { data: { isVarified: true, active: true } }, isLoading: false, isFetching: false };
  h.me = { data: { data: { isVarified: true, active: true } }, isLoading: false };
});

describe("UserMFAConfirmationEnable", () => {
  it("blocks opening the dialog and toasts when the user is not verified", () => {
    h.userById = { data: { data: { isVarified: false, active: true } }, isLoading: false, isFetching: false };
    render(<UserMFAConfirmationEnable />);
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    expect(h.toast).toHaveBeenCalledWith(
      expect.objectContaining({ description: "Please verify the user first" }),
    );
  });

  it("opens the dialog, selects a method and enables MFA", async () => {
    h.configure.mockResolvedValue({ isSuccess: true });
    render(<UserMFAConfirmationEnable />);
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    await waitFor(() => expect(screen.getByText("Enable MFA?")).toBeInTheDocument());
    fireEvent.click(screen.getByText("select-method"));
    await waitFor(() => expect(screen.getByRole("button", { name: "Save" })).toBeEnabled());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.configure).toHaveBeenCalledWith({ mfaEnabled: true, userId: "u1", userMfaType: 2 }),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
    expect(h.showVerify).toHaveBeenCalledWith(2);
  });

  it("shows an error toast when enabling fails", async () => {
    h.configure.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<UserMFAConfirmationEnable />);
    fireEvent.click(screen.getByRole("button", { name: "Enable" }));
    await waitFor(() => expect(screen.getByText("Enable MFA?")).toBeInTheDocument());
    fireEvent.click(screen.getByText("select-method"));
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "nope" }));
  });
});
