import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  mockProjectStoreFactory,
  mockSelectedProject,
  TEST_TENANT_ID,
} from "@/test-utils/__mocks__";
import { notificationService } from "../services/notification.service";
import { useProjectStore } from "@/store/useProjectStore";
import {
  useGetNotifications,
  useMarkAsRead,
  useMarkAllAsRead,
  useGetBlocksNotificationConfig,
  useGetNotificationConfigs,
  useSaveNotificationConfig,
  useDeleteNotificationConfig,
} from "./use-notifications";

vi.mock("../services/notification.service", () => ({
  notificationService: {
    getNotifications: vi.fn(),
    markAsRead: vi.fn(),
    markAllNotificationsAsRead: vi.fn(),
    getNotificationConfig: vi.fn(),
    getNotificationConfigs: vi.fn(),
    saveNotificationConfig: vi.fn(),
    deleteNotificationConfig: vi.fn(),
  },
}));

vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

// ─── Inline mock data ────────────────────────────────────────────────────────
const mockNotificationsResponse = {
  unReadNotificationsCount: 1,
  totalNotificationsCount: 3,
  notifications: [],
};

const mockSuccess = { errors: null, isSuccess: true };

const mockConfigsResponse = {
  configurations: [],
  totalCount: 0,
  errors: null,
  isSuccess: true,
};

const mockSaveConfigPayload = {
  name: "cfg",
  channelToNotify: 1,
  notificationType: 1,
  enablePersistence: true,
  notifyMethod: "onSomething",
  projectKey: TEST_TENANT_ID,
  isUpdateRequest: false,
};

describe("Notification Hooks (notifications)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useProjectStore).mockReturnValue({
      selectedProject: mockSelectedProject,
    } as never);
  });

  // ─── useGetNotifications ───────────────────────────────────────────────────
  describe("useGetNotifications", () => {
    it("should fetch notifications successfully", async () => {
      vi.mocked(notificationService.getNotifications).mockResolvedValue(mockNotificationsResponse);

      const { result } = renderHook(() => useGetNotifications(1, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockNotificationsResponse);
      expect(notificationService.getNotifications).toHaveBeenCalledWith(1, 10);
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.getNotifications).mockRejectedValue(new Error("Fetch failed"));

      const { result } = renderHook(() => useGetNotifications(1, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useMarkAsRead ─────────────────────────────────────────────────────────
  describe("useMarkAsRead", () => {
    it("should mark a notification as read", async () => {
      vi.mocked(notificationService.markAsRead).mockResolvedValue(mockSuccess);

      const { result } = renderHook(() => useMarkAsRead(), { wrapper: createWrapper() });

      result.current.mutate("notif-1");

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(notificationService.markAsRead).toHaveBeenCalledWith("notif-1", expect.anything());
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.markAsRead).mockRejectedValue(new Error("Mark failed"));

      const { result } = renderHook(() => useMarkAsRead(), { wrapper: createWrapper() });

      result.current.mutate("notif-1");

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useMarkAllAsRead ──────────────────────────────────────────────────────
  describe("useMarkAllAsRead", () => {
    it("should mark all notifications as read", async () => {
      vi.mocked(notificationService.markAllNotificationsAsRead).mockResolvedValue(mockSuccess);

      const { result } = renderHook(() => useMarkAllAsRead(), { wrapper: createWrapper() });

      result.current.mutate();

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(notificationService.markAllNotificationsAsRead).toHaveBeenCalled();
    });

    it("should invalidate notifications on success", async () => {
      vi.mocked(notificationService.markAllNotificationsAsRead).mockResolvedValue(mockSuccess);
      vi.mocked(notificationService.getNotifications).mockResolvedValue(mockNotificationsResponse);

      const wrapper = createWrapper();

      const { result: listResult } = renderHook(() => useGetNotifications(1, 10), { wrapper });
      await waitFor(() => expect(listResult.current.isSuccess).toBe(true));

      const { result: markResult } = renderHook(() => useMarkAllAsRead(), { wrapper });
      markResult.current.mutate();

      await waitFor(() => expect(markResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(notificationService.getNotifications).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.markAllNotificationsAsRead).mockRejectedValue(
        new Error("Mark all failed"),
      );

      const { result } = renderHook(() => useMarkAllAsRead(), { wrapper: createWrapper() });

      result.current.mutate();

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetBlocksNotificationConfig ────────────────────────────────────────
  describe("useGetBlocksNotificationConfig", () => {
    it("should fetch blocks notification configs successfully", async () => {
      vi.mocked(notificationService.getNotificationConfigs).mockResolvedValue(mockConfigsResponse);

      const { result } = renderHook(() => useGetBlocksNotificationConfig(0, 100), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockConfigsResponse);
      expect(notificationService.getNotificationConfigs).toHaveBeenCalledWith(
        0,
        100,
        expect.any(String),
      );
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.getNotificationConfigs).mockRejectedValue(
        new Error("Fetch failed"),
      );

      const { result } = renderHook(() => useGetBlocksNotificationConfig(0, 100), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetNotificationConfigs ─────────────────────────────────────────────
  describe("useGetNotificationConfigs", () => {
    it("should fetch notification configs with the tenant id from the store", async () => {
      vi.mocked(notificationService.getNotificationConfigs).mockResolvedValue(mockConfigsResponse);

      const { result } = renderHook(() => useGetNotificationConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(notificationService.getNotificationConfigs).toHaveBeenCalledWith(
        0,
        10,
        TEST_TENANT_ID,
      );
    });

    it("should be disabled when there is no tenant id", async () => {
      vi.mocked(useProjectStore).mockReturnValue({
        selectedProject: { tenantId: "" },
      } as never);
      vi.mocked(notificationService.getNotificationConfigs).mockResolvedValue(mockConfigsResponse);

      const { result } = renderHook(() => useGetNotificationConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(notificationService.getNotificationConfigs).not.toHaveBeenCalled();
    });
  });

  // ─── useSaveNotificationConfig ─────────────────────────────────────────────
  describe("useSaveNotificationConfig", () => {
    it("should save a notification config successfully", async () => {
      vi.mocked(notificationService.saveNotificationConfig).mockResolvedValue(mockSuccess);

      const { result } = renderHook(() => useSaveNotificationConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(notificationService.saveNotificationConfig).toHaveBeenCalledWith(
        mockSaveConfigPayload,
        expect.anything(),
      );
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.saveNotificationConfig).mockRejectedValue(
        new Error("Save failed"),
      );

      const { result } = renderHook(() => useSaveNotificationConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useDeleteNotificationConfig ───────────────────────────────────────────
  describe("useDeleteNotificationConfig", () => {
    it("should delete a notification config successfully", async () => {
      vi.mocked(notificationService.deleteNotificationConfig).mockResolvedValue(mockSuccess);

      const { result } = renderHook(() => useDeleteNotificationConfig(), {
        wrapper: createWrapper(),
      });

      const payload = { itemId: "cfg-1", projectKey: TEST_TENANT_ID };
      result.current.mutate(payload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(notificationService.deleteNotificationConfig).toHaveBeenCalledWith(
        payload,
        expect.anything(),
      );
    });

    it("should handle errors", async () => {
      vi.mocked(notificationService.deleteNotificationConfig).mockRejectedValue(
        new Error("Delete failed"),
      );

      const { result } = renderHook(() => useDeleteNotificationConfig(), {
        wrapper: createWrapper(),
      });

      result.current.mutate({ itemId: "cfg-1", projectKey: TEST_TENANT_ID });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
