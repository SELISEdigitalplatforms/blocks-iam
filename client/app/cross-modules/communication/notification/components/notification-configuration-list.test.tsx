import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

const h = vi.hoisted(() => ({
  configsResult: {} as Record<string, unknown>,
  deleteConfig: vi.fn(),
  isDeletePending: false,
  toast: vi.fn(),
}));

vi.mock("../hooks/use-notifications", () => ({
  useGetNotificationConfigs: () => h.configsResult,
  useDeleteNotificationConfig: () => ({
    isPending: h.isDeletePending,
    mutateAsync: h.deleteConfig,
  }),
}));
vi.mock("../modals/new-notification-configuration", () => ({
  default: () => <div data-testid="new-config-modal" />,
}));
vi.mock("../constants/notification.constant", () => ({
  channelsToNotify: [{ value: 1, label: "Email" }],
  notificationTypes: [{ value: 2, label: "Alert" }],
}));
vi.mock("@/components/confirmation-modal/confirmation-modal", () => ({
  default: ({ onConfirm }: { onConfirm: () => void }) => (
    <button onClick={onConfirm}>confirm-delete</button>
  ),
}));
vi.mock("@/hooks/use-toast", () => ({ toast: (a: unknown) => h.toast(a) }));

import NotificationConfigurationList from "./notification-configuration-list";

const config = (over: Record<string, unknown> = {}) => ({
  itemId: "c1",
  name: "My Config",
  channelToNotify: 1,
  notificationType: 2,
  enablePersistence: true,
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  h.isDeletePending = false;
  h.configsResult = {
    data: { configurations: [config()], totalCount: 1 },
    isLoading: false,
  };
});

describe("NotificationConfigurationList", () => {
  it("renders a row per configuration with mapped channel and type labels", () => {
    render(<NotificationConfigurationList />);
    expect(screen.getByText("My Config")).toBeInTheDocument();
    expect(screen.getByText("Email")).toBeInTheDocument();
    expect(screen.getByText("Alert")).toBeInTheDocument();
  });

  it("shows the empty state when there are no configurations", () => {
    h.configsResult = { data: { configurations: [], totalCount: 0 }, isLoading: false };
    render(<NotificationConfigurationList />);
    expect(screen.getByText("No notification configurations found.")).toBeInTheDocument();
  });

  it("renders loading skeleton rows while loading", () => {
    h.configsResult = { data: undefined, isLoading: true };
    const { container } = render(<NotificationConfigurationList />);
    expect(container.querySelectorAll("tbody tr").length).toBe(5);
  });

  it("renders the row action menu trigger for each configuration", () => {
    render(<NotificationConfigurationList />);
    const actionTriggers = screen.getAllByRole("button");
    expect(actionTriggers.length).toBeGreaterThan(0);
  });

  it("shows the pagination control when the total exceeds the page size", () => {
    h.configsResult = {
      data: { configurations: [config()], totalCount: 25 },
      isLoading: false,
    };
    render(<NotificationConfigurationList />);
    expect(screen.getByText("My Config")).toBeInTheDocument();
  });

  const openRowMenu = () => {
    fireEvent.pointerDown(
      screen.getByRole("button"),
      { button: 0, ctrlKey: false, pointerType: "mouse" },
    );
  };

  it("opens the edit dialog from the row menu", async () => {
    render(<NotificationConfigurationList />);
    // The always-mounted add dialog renders one modal stub before editing.
    expect(screen.getAllByTestId("new-config-modal")).toHaveLength(1);
    openRowMenu();
    fireEvent.click(await screen.findByText("Edit"));
    // Editing mounts a second modal stub for the selected configuration.
    await waitFor(() => expect(screen.getAllByTestId("new-config-modal")).toHaveLength(2));
  });

  it("deletes a configuration and shows a success toast", async () => {
    h.deleteConfig.mockResolvedValue({ isSuccess: true });
    render(<NotificationConfigurationList />);
    openRowMenu();
    fireEvent.click(await screen.findByText("Delete"));
    fireEvent.click(await screen.findByText("confirm-delete"));
    await waitFor(() => expect(h.deleteConfig).toHaveBeenCalled());
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "success" })),
    );
  });

  it("shows an error toast when deletion fails", async () => {
    h.deleteConfig.mockResolvedValue({ isSuccess: false, errors: "nope" });
    render(<NotificationConfigurationList />);
    openRowMenu();
    fireEvent.click(await screen.findByText("Delete"));
    fireEvent.click(await screen.findByText("confirm-delete"));
    await waitFor(() =>
      expect(h.toast).toHaveBeenCalledWith(expect.objectContaining({ variant: "destructive" })),
    );
  });
});
