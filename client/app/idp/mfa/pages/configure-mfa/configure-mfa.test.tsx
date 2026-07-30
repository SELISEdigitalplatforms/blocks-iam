import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  useGetMFAConfig: vi.fn(),
  useSaveMFAConfig: vi.fn(),
  mutateAsync: vi.fn(),
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("../../hooks/use-mfa-config", () => ({
  useGetMFAConfig: h.useGetMFAConfig,
  useSaveMFAConfig: h.useSaveMFAConfig,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
  showSuccessToast: h.showSuccessToast,
}));

import { ConfigureMFA } from "./configure-mfa";

function setConfig({
  allowedMethods = [] as number[],
  isLoading = false,
  isFetching = false,
  data = { allowedMethods } as { allowedMethods: number[] } | undefined,
}: {
  allowedMethods?: number[];
  isLoading?: boolean;
  isFetching?: boolean;
  data?: { allowedMethods: number[] } | undefined;
} = {}) {
  h.useGetMFAConfig.mockReturnValue({ isLoading, isFetching, data });
}

beforeEach(() => {
  vi.clearAllMocks();
  setConfig({ allowedMethods: [] });
  h.useSaveMFAConfig.mockReturnValue({ isPending: false, mutateAsync: h.mutateAsync });
});

describe("ConfigureMFA", () => {
  it("shows the loading skeleton while fetching", () => {
    setConfig({ isLoading: true, data: undefined });
    const { container } = render(<ConfigureMFA />);
    expect(container.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the empty state when there is no config data", () => {
    h.useGetMFAConfig.mockReturnValue({ isLoading: false, isFetching: false, data: undefined });
    render(<ConfigureMFA />);
    expect(
      screen.getByText(/MFA is not yet configured for this project/),
    ).toBeInTheDocument();
  });

  it("lists the providers with their enabled/disabled status", () => {
    setConfig({ allowedMethods: [2] });
    render(<ConfigureMFA />);
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("Authenticator app")).toBeInTheDocument();
    expect(screen.getByText("Enabled")).toBeInTheDocument();
    expect(screen.getByText("Disabled")).toBeInTheDocument();
  });

  it("enables a disabled provider and shows a success toast", async () => {
    const user = userEvent.setup();
    setConfig({ allowedMethods: [] });
    h.mutateAsync.mockResolvedValue({ isSuccess: true });
    render(<ConfigureMFA />);

    // Open the first row's action menu and click Enable.
    await user.click(screen.getAllByRole("button")[0]);
    await user.click(await screen.findByText("Enable"));

    // Confirm in the dialog.
    fireEvent.click(await screen.findByRole("button", { name: /confirm|yes/i }));

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({
        enableMfa: true,
        userMfaType: [2],
      }),
    );
    expect(h.showSuccessToast).toHaveBeenCalled();
  });

  it("surfaces an error toast when the save fails", async () => {
    const user = userEvent.setup();
    setConfig({ allowedMethods: [] });
    h.mutateAsync.mockResolvedValue({ isSuccess: false, errors: { m: "bad" } });
    render(<ConfigureMFA />);

    await user.click(screen.getAllByRole("button")[0]);
    await user.click(await screen.findByText("Enable"));
    fireEvent.click(await screen.findByRole("button", { name: /confirm|yes/i }));

    await waitFor(() => expect(h.showErrorToast).toHaveBeenCalled());
    expect(h.showSuccessToast).not.toHaveBeenCalled();
  });
});
