import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  authConfig: {} as Record<string, unknown>,
  save: vi.fn(),
  isPending: false,
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@seliseblocks/blocks-kit", async (importActual) => {
  const actual = await importActual<Record<string, unknown>>();
  return { ...actual, useProjectStore: () => ({ selectedProject: { tenantId: "tenant-1" } }) };
});
vi.mock("@blocks-idp/authentication/hooks/use-auth-config", () => ({
  useGetAuthConfig: () => ({ data: h.authConfig }),
  useSaveAuthConfig: () => ({ mutateAsync: h.save, isPending: h.isPending }),
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));

import { EditGeneralSettings } from "./edit-settings";

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.authConfig = {
    refreshTokenValidForNumberMinutes: 60,
    getNumberOfWrongAttemptsToLockTheAccount: 5,
    accountLockDurationInMinutes: 30,
    accessTokenValidForNumberMinutes: 15,
    rememberMeRefreshTokenValidForNumberMinutes: 120,
  };
});

describe("EditGeneralSettings", () => {
  it("opens the settings dialog prefilled from the auth config", async () => {
    render(<EditGeneralSettings />);
    fireEvent.click(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Settings")).toBeInTheDocument());
    expect(screen.getByPlaceholderText("Enter number")).toBeInTheDocument();
  });

  it("saves updated settings and shows a success toast", async () => {
    h.save.mockResolvedValue({ isSuccess: true });
    render(<EditGeneralSettings />);
    fireEvent.click(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Settings")).toBeInTheDocument());
    const accessTokenInput = screen.getByPlaceholderText("Enter number");
    fireEvent.input(accessTokenInput, { target: { value: "25" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() =>
      expect(h.save).toHaveBeenCalledWith(
        expect.objectContaining({ projectKey: "tenant-1" }),
      ),
    );
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it("shows an error toast when the save fails", async () => {
    h.save.mockResolvedValue({ isSuccess: false, errors: "bad values" });
    render(<EditGeneralSettings />);
    fireEvent.click(screen.getByText("Edit"));
    await waitFor(() => expect(screen.getByText("Settings")).toBeInTheDocument());
    fireEvent.input(screen.getByPlaceholderText("Enter number"), {
      target: { value: "25" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(h.showError).toHaveBeenCalledWith({ errors: "bad values" }));
  });
});
