import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  useSessionDetails: vi.fn(),
  useRevokeSession: vi.fn(),
  toViewModel: vi.fn(),
  mutateAsync: vi.fn(),
  showErrorToast: vi.fn(),
  showSuccessToast: vi.fn(),
}));

vi.mock("../hooks", () => ({
  useSessionDetails: h.useSessionDetails,
  useRevokeSession: h.useRevokeSession,
}));
vi.mock("../mappers/session.mapper", () => ({
  toSessionDetailsViewModel: h.toViewModel,
}));
vi.mock("@blocks-idp/iam/utils/device-icon", () => ({
  getDeviceIcon: () => () => <svg data-testid="device-icon" />,
}));
vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: h.showErrorToast,
  showSuccessToast: h.showSuccessToast,
}));

import { SessionDetailsDrawer } from "./session-details-drawer";

const vm = {
  overview: {
    operatingSystem: "macOS",
    deviceName: "Jane's MacBook",
    isCurrent: false,
    statusLabel: "Active",
    sessionId: "sess-1",
    ipAddress: "10.0.0.1",
    startedAtDisplay: "1 Jan 2026",
    browser: "Chrome",
    absoluteExpiryDisplay: "8 Jan 2026",
  },
  applications: [
    { clientName: "Console", rotationCountLabel: "3", statusLabel: "Connected" },
  ],
  timeline: [
    { type: "login", tone: "info", label: "Signed in", timestampDisplay: "1 Jan", secondary: "" },
  ],
};

function setDetails({
  data = {} as unknown,
  isLoading = false,
  isError = false,
  error = undefined as unknown,
} = {}) {
  h.useSessionDetails.mockReturnValue({ data, isLoading, isError, error });
}

const renderDrawer = (props: Partial<React.ComponentProps<typeof SessionDetailsDrawer>> = {}) =>
  render(
    <SessionDetailsDrawer
      sessionId={props.sessionId === undefined ? "sess-1" : props.sessionId}
      onOpenChange={props.onOpenChange ?? vi.fn()}
      onRevoked={props.onRevoked}
      userId={props.userId ?? "user-1"}
    />,
  );

beforeEach(() => {
  vi.clearAllMocks();
  setDetails({ data: { raw: true } });
  h.toViewModel.mockReturnValue(vm);
  h.useRevokeSession.mockReturnValue({ mutateAsync: h.mutateAsync, isPending: false });
});

describe("SessionDetailsDrawer", () => {
  it("renders the session overview from the view model", () => {
    renderDrawer();
    expect(screen.getByText("Jane's MacBook")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("10.0.0.1")).toBeInTheDocument();
    expect(screen.getByText("Chrome on macOS")).toBeInTheDocument();
    expect(screen.getByText("Console")).toBeInTheDocument();
    expect(screen.getByText("Signed in")).toBeInTheDocument();
  });

  it("shows a loading skeleton while the session is loading", () => {
    setDetails({ isLoading: true, data: undefined });
    renderDrawer();
    // Sheet content renders in a portal, so query the whole document.
    expect(document.querySelectorAll(".animate-pulse").length).toBeGreaterThan(0);
  });

  it("shows the error state when the session fails to load", () => {
    setDetails({ isError: true, error: new Error("network down"), data: undefined });
    renderDrawer();
    expect(screen.getByText("Unable to load session")).toBeInTheDocument();
    expect(screen.getByText("network down")).toBeInTheDocument();
  });

  it("shows the not-found state when there is no view model", () => {
    h.toViewModel.mockReturnValue(null);
    setDetails({ data: null });
    renderDrawer();
    expect(screen.getByText("Session not found")).toBeInTheDocument();
  });

  it("revokes the session through the confirmation dialog", async () => {
    h.mutateAsync.mockResolvedValue(undefined);
    const onRevoked = vi.fn();
    const onOpenChange = vi.fn();
    renderDrawer({ onRevoked, onOpenChange });

    fireEvent.click(screen.getByRole("button", { name: /sign out of this device/i }));
    // The dialog confirm button reads "Sign out".
    const confirm = await screen.findByRole("button", { name: /^sign out$/i });
    fireEvent.click(confirm);

    await waitFor(() =>
      expect(h.mutateAsync).toHaveBeenCalledWith({ sessionId: "sess-1" }),
    );
    expect(h.showSuccessToast).toHaveBeenCalled();
    await waitFor(() => expect(onRevoked).toHaveBeenCalled());
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("surfaces an error toast when revoking fails", async () => {
    h.mutateAsync.mockRejectedValue({ errors: "cannot revoke" });
    renderDrawer();

    fireEvent.click(screen.getByRole("button", { name: /sign out of this device/i }));
    fireEvent.click(await screen.findByRole("button", { name: /^sign out$/i }));

    await waitFor(() =>
      expect(h.showErrorToast).toHaveBeenCalledWith({ errors: "cannot revoke" }),
    );
  });

  it("hides the sign-out control for the current device", () => {
    h.toViewModel.mockReturnValue({
      ...vm,
      overview: { ...vm.overview, isCurrent: true },
    });
    renderDrawer();
    expect(screen.getByText("This device")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /sign out of this device/i }),
    ).not.toBeInTheDocument();
  });
});
