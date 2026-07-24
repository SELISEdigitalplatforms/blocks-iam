import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  settingData: undefined as unknown,
  save: vi.fn(),
  isPending: false,
}));

vi.mock("@blocks-idp/iam/hooks/use-user", () => ({
  useGetSignUpSetting: () => ({ data: h.settingData }),
  useSaveSignUpSetting: () => ({ mutateAsync: h.save, isPending: h.isPending }),
}));

import { SignupSettings } from "./signup-settings";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.settingData = {
    isEmailPasswordSignUpEnabled: true,
    isSSoSignUpEnabled: false,
    isSignUpEnable: true,
    defaultRolesForNewUser: ["member"],
    defaultPermissionsForNewUser: [],
  };
});

describe("SignupSettings", () => {
  it("opens the dialog with the settings from the server", async () => {
    render(<SignupSettings />);
    fireEvent.click(screen.getByText("Signup Settings"));
    await waitFor(() => expect(screen.getByText("Allow signup")).toBeInTheDocument());
    expect((screen.getByLabelText("Allow signup") as HTMLInputElement)).toBeChecked();
  });

  it("saves the settings and closes on save", async () => {
    h.save.mockResolvedValue({ isSuccess: true });
    render(<SignupSettings />);
    fireEvent.click(screen.getByText("Signup Settings"));
    await waitFor(() => expect(screen.getByText("Allow signup")).toBeInTheDocument());
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith(
        expect.objectContaining({
          isEmailPasswordSignUpEnabled: true,
          isSSoSignUpEnabled: false,
          defaultRolesForNewUserOnSignUp: ["member"],
        }),
      ),
    );
  });

  it("disables the sub-options and save when allow-signup is turned off", async () => {
    render(<SignupSettings />);
    fireEvent.click(screen.getByText("Signup Settings"));
    await waitFor(() => expect(screen.getByLabelText("Allow signup")).toBeInTheDocument());
    fireEvent.click(screen.getByLabelText("Allow signup"));
    expect(screen.getByLabelText("Email and password")).toBeDisabled();
  });
});
