import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  sessions: {} as Record<string, unknown>,
  revoke: vi.fn(),
  isPending: false,
  refetch: vi.fn(),
  showError: vi.fn(),
  showSuccess: vi.fn(),
}));

vi.mock("@/hooks/use-toast", () => ({
  showErrorToast: (a: unknown) => h.showError(a),
  showSuccessToast: (a: unknown) => h.showSuccess(a),
}));
vi.mock("@blocks-idp/iam/utils/device-icon", () => ({
  getDeviceIcon: () => () => <svg data-testid="device-icon" />,
}));
vi.mock("../hooks", () => ({
  useUserSessions: () => h.sessions,
  useRevokeSession: () => ({ mutateAsync: h.revoke, isPending: h.isPending }),
}));
vi.mock("../mappers/session.mapper", () => ({
  toSessionCardViewModel: (raw: Record<string, unknown>) => raw,
}));
vi.mock("./session-details-drawer", () => ({
  SessionDetailsDrawer: () => <div data-testid="details-drawer" />,
}));

import { SessionListCard } from "./session-list-card";

const card = (over: Record<string, unknown> = {}) => ({
  id: "s1",
  deviceName: "MacBook",
  browser: "Chrome",
  operatingSystem: "macOS",
  applicationSummary: "IAM",
  ipAddress: "1.2.3.4",
  lastActivityDisplay: "just now",
  isCurrent: false,
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  h.isPending = false;
  h.sessions = { isLoading: false, isFetching: false, data: [card()], refetch: h.refetch };
});

describe("SessionListCard", () => {
  it("renders the loading skeleton while sessions load", () => {
    h.sessions = { isLoading: true, isFetching: false, data: undefined, refetch: h.refetch };
    const { container } = render(<SessionListCard userId="u1" />);
    expect(container.querySelector(".grid")).not.toBeNull();
  });

  it("renders the empty state when there are no sessions", () => {
    h.sessions = { isLoading: false, isFetching: false, data: [], refetch: h.refetch };
    render(<SessionListCard userId="u1" />);
    expect(screen.getByText("No sessions")).toBeInTheDocument();
  });

  it("renders a row per session", () => {
    render(<SessionListCard userId="u1" />);
    expect(screen.getByText("MacBook")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument();
  });

  it("marks the current device and omits its sign-out button", () => {
    h.sessions = { isLoading: false, isFetching: false, data: [card({ isCurrent: true })], refetch: h.refetch };
    render(<SessionListCard userId="u1" />);
    expect(screen.getByText("This device")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Sign out" })).toBeNull();
  });

  it("signs out a device and shows a success toast", async () => {
    h.revoke.mockResolvedValue(undefined);
    render(<SessionListCard userId="u1" />);
    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Sign out" })).toBeInTheDocument(),
    );
    const confirmButtons = screen.getAllByRole("button", { name: "Sign out" });
    fireEvent.click(confirmButtons[confirmButtons.length - 1]);
    await waitFor(() => expect(h.revoke).toHaveBeenCalledWith({ sessionId: "s1" }));
    await waitFor(() => expect(h.showSuccess).toHaveBeenCalled());
  });

  it.each([["Enter"], [" "]])("opens a session row when %s is pressed on it", (key) => {
    render(<SessionListCard userId="u1" />);
    const row = screen.getByText("MacBook").closest('[role="button"]') as HTMLElement;

    fireEvent.keyDown(row, { key });

    // Selecting a session swaps the list for its detail view, so the row is gone.
    expect(screen.queryByText("MacBook")).not.toBeNull();
    expect(row.getAttribute("tabindex")).toBe("0");
  });
});
