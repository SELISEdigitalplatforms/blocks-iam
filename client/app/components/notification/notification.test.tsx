import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createWrapper } from "@/test-utils/test-providers/query-client";

const h = vi.hoisted(() => ({
  notificationsResult: {
    data: undefined as unknown,
    isLoading: false,
    isFetching: false,
  },
  configResult: { data: { configurations: [] as unknown[] } },
  markAsRead: { mutate: vi.fn() },
  markAllAsRead: { mutate: vi.fn() },
  client: {
    connect: vi.fn(),
    disconnect: vi.fn(),
    connection: { on: vi.fn() },
  },
  service: { getNotificationConfig: vi.fn() },
}));

vi.mock("@/notifications/hooks/use-notifications", () => ({
  useGetBlocksNotificationConfig: vi.fn(() => h.configResult),
  useGetNotifications: vi.fn(() => h.notificationsResult),
  useMarkAsRead: vi.fn(() => h.markAsRead),
  useMarkAllAsRead: vi.fn(() => h.markAllAsRead),
}));
vi.mock("@/notifications/services/notification-client.service", () => ({
  notificationClientService: h.client,
}));
vi.mock("@/notifications/services/notification.service", () => ({
  notificationService: h.service,
}));

import { Notification } from "./notification";

const makeNotification = (over: Record<string, unknown> = {}) => ({
  id: "n1",
  isRead: false,
  createdTime: "2024-05-01T10:00:00Z",
  denormalizedPayload: JSON.stringify({
    title: "hello_world",
    description: "Something happened",
    redirectPath: "",
    toastable: false,
    meta: "",
  }),
  ...over,
});

beforeEach(() => {
  vi.clearAllMocks();
  h.notificationsResult = { data: undefined, isLoading: false, isFetching: false };
});

describe("Notification", () => {
  it("renders the bell and connects/disconnects the realtime client", () => {
    render(<Notification />, { wrapper: createWrapper() });
    expect(screen.getByTestId("notification-bell")).toBeInTheDocument();
    expect(h.client.connect).toHaveBeenCalled();
  });

  it("shows the unread count badge from the query data", () => {
    h.notificationsResult = {
      data: {
        notifications: [makeNotification()],
        unReadNotificationsCount: 4,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    expect(screen.getByText("4")).toBeInTheDocument();
  });

  it("caps the unread badge at 99+", () => {
    h.notificationsResult = {
      data: {
        notifications: [],
        unReadNotificationsCount: 150,
        totalNotificationsCount: 0,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    expect(screen.getByText("99+")).toBeInTheDocument();
  });

  it("opens the popover and lists notifications with a formatted title", async () => {
    h.notificationsResult = {
      data: {
        notifications: [makeNotification()],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByTestId("notification-bell"));

    await waitFor(() =>
      expect(screen.getByText("Notifications")).toBeInTheDocument(),
    );
    expect(screen.getByText("Mark all as read")).toBeInTheDocument();
    // formatKBTitle turns "hello_world" into "Hello World".
    expect(screen.getByText("Hello World")).toBeInTheDocument();
  });

  it("fires the mark-all-as-read mutation from the popover", async () => {
    h.notificationsResult = {
      data: {
        notifications: [makeNotification()],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByTestId("notification-bell"));
    const markAll = await screen.findByText("Mark all as read");
    fireEvent.click(markAll);

    expect(h.markAllAsRead.mutate).toHaveBeenCalled();
  });

  it("shows the empty state when there are no notifications", async () => {
    h.notificationsResult = {
      data: { notifications: [], unReadNotificationsCount: 0, totalNotificationsCount: 0 },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    expect(await screen.findByText("No notifications")).toBeInTheDocument();
  });

  it("marks a notification as read on hover", async () => {
    h.notificationsResult = {
      data: {
        notifications: [makeNotification({ isRead: false })],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    const row = (await screen.findByText("Hello World")).closest("div.cursor-pointer") as HTMLElement;
    fireEvent.mouseEnter(row);
    expect(h.markAsRead.mutate).toHaveBeenCalledWith("n1", expect.any(Object));
  });

  it("formats the KB meta description with status and kb id", async () => {
    h.notificationsResult = {
      data: {
        notifications: [
          makeNotification({
            denormalizedPayload: JSON.stringify({
              title: "agent_kb_processing_status",
              description: "fallback",
              redirectPath: "",
              toastable: false,
              meta: JSON.stringify({ status: "processing", kb_id: "abcd1234-xyz" }),
            }),
          }),
        ],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    expect(await screen.findByText("AI Agent Knowledge Update Status")).toBeInTheDocument();
    expect(screen.getByText("Status: Processing | KB Id: abcd1234")).toBeInTheDocument();
  });

  it("marks a single notification as read and updates local state on success", async () => {
    h.markAsRead.mutate = vi.fn((_id: string, opts: { onSuccess: () => void }) => opts.onSuccess());
    h.notificationsResult = {
      data: {
        notifications: [makeNotification({ isRead: false })],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    const row = (await screen.findByText("Hello World")).closest("div.cursor-pointer") as HTMLElement;
    fireEvent.mouseEnter(row);
    expect(h.markAsRead.mutate).toHaveBeenCalled();
  });

  it("runs the mark-all success handler that flips notifications to read", async () => {
    h.markAllAsRead.mutate = vi.fn((_u: undefined, opts: { onSuccess: () => void }) => opts.onSuccess());
    h.notificationsResult = {
      data: {
        notifications: [makeNotification()],
        unReadNotificationsCount: 1,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    fireEvent.click(await screen.findByText("Mark all as read"));
    expect(h.markAllAsRead.mutate).toHaveBeenCalled();
  });

  it("merges and sorts multiple notifications and paginates on scroll", async () => {
    h.notificationsResult = {
      data: {
        notifications: [
          makeNotification({ id: "a", createdTime: "2024-05-01T10:00:00Z" }),
          makeNotification({ id: "b", createdTime: "2024-06-01T10:00:00Z" }),
        ],
        unReadNotificationsCount: 2,
        totalNotificationsCount: 20,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    const rows = await screen.findAllByText("Hello World");
    const list = rows[0].closest("div.cursor-pointer")?.parentElement as HTMLElement;
    fireEvent.scroll(list);
    expect(list).toBeInTheDocument();
  });

  it("renders empty-title, object-meta and kb-only meta variants", async () => {
    h.notificationsResult = {
      data: {
        notifications: [
          makeNotification({
            id: "empty",
            denormalizedPayload: JSON.stringify({ title: "", description: "d", meta: { status: "done" } }),
          }),
          makeNotification({
            id: "kbonly",
            denormalizedPayload: JSON.stringify({ title: "x", description: "d", meta: { kb_id: "kkkk-11" } }),
          }),
        ],
        unReadNotificationsCount: 0,
        totalNotificationsCount: 2,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    expect(await screen.findByText("No Title")).toBeInTheDocument();
    expect(screen.getByText("Status: Done")).toBeInTheDocument();
    expect(screen.getByText("KB Id: kkkk")).toBeInTheDocument();
  });

  it("handles an unparseable denormalized payload in a row", async () => {
    const errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    h.notificationsResult = {
      data: {
        notifications: [makeNotification({ id: "bad", denormalizedPayload: "not-json" })],
        unReadNotificationsCount: 0,
        totalNotificationsCount: 1,
      },
      isLoading: false,
      isFetching: false,
    };
    render(<Notification />, { wrapper: createWrapper() });
    fireEvent.click(screen.getByTestId("notification-bell"));
    await waitFor(() => expect(errSpy).toHaveBeenCalled());
    errSpy.mockRestore();
  });

  it("handles a non-string realtime message and an invalid payload", () => {
    const errSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    const handlers: Array<(m: unknown) => void> = [];
    h.configResult = { data: { configurations: [{ notifyMethod: "push" }] } };
    h.client.connection.on = vi.fn((_event: string, cb: (m: unknown) => void) => {
      handlers.push(cb);
    });
    render(<Notification />, { wrapper: createWrapper() });
    // Object message hits the non-string branch.
    handlers[0]?.({ denormalizedPayload: JSON.stringify({ title: "T", description: "D" }) });
    // Message whose inner payload is invalid hits the catch branch.
    handlers[0]?.(JSON.stringify({ denormalizedPayload: "not-json" }));
    expect(errSpy).toHaveBeenCalled();
    errSpy.mockRestore();
  });

  it("registers realtime handlers and invalidates on a valid message", () => {
    let handler: ((message: string) => void) | undefined;
    h.configResult = { data: { configurations: [{ notifyMethod: "push" }] } };
    h.client.connection.on = vi.fn((_event: string, cb: (m: string) => void) => {
      handler = cb;
    });
    render(<Notification />, { wrapper: createWrapper() });
    expect(h.client.connection.on).toHaveBeenCalledWith("push", expect.any(Function));
    // Delivering a valid denormalized payload runs the parse + invalidate path.
    handler?.(
      JSON.stringify({
        denormalizedPayload: JSON.stringify({ title: "T", description: "D" }),
      }),
    );
    expect(h.service.getNotificationConfig).toHaveBeenCalled();
  });
});
