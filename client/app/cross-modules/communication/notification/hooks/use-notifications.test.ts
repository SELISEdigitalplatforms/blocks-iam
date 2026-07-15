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
  useGetNotificationConfigs,
  useSaveNotificationConfig,
  useDeleteNotificationConfig,
} from "./use-notifications";

vi.mock("../services/notification.service", () => ({
  notificationService: {
    getNotificationConfigs: vi.fn(),
    saveNotificationConfig: vi.fn(),
    deleteNotificationConfig: vi.fn(),
  },
}));

vi.mock("@/store/useProjectStore", () => mockProjectStoreFactory());

// ─── Inline mock data ────────────────────────────────────────────────────────
const mockConfigsResponse = {
  configurations: [],
  totalCount: 0,
  errors: null,
  isSuccess: true,
};

const mockSuccess = { errors: null, isSuccess: true };

const mockSaveConfigPayload = {
  name: "cfg",
  channelToNotify: 1,
  notificationType: 1,
  enablePersistence: true,
  notifyMethod: "onSomething",
  projectKey: TEST_TENANT_ID,
  isUpdateRequest: false,
};

describe("Notification Hooks (communication)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(useProjectStore).mockReturnValue({
      selectedProject: mockSelectedProject,
    } as never);
  });

  // ─── useGetNotificationConfigs ─────────────────────────────────────────────
  describe("useGetNotificationConfigs", () => {
    it("should fetch notification configs with the tenant id from the store", async () => {
      vi.mocked(notificationService.getNotificationConfigs).mockResolvedValue(mockConfigsResponse);

      const { result } = renderHook(() => useGetNotificationConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockConfigsResponse);
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

    it("should handle errors", async () => {
      vi.mocked(notificationService.getNotificationConfigs).mockRejectedValue(
        new Error("Fetch failed"),
      );

      const { result } = renderHook(() => useGetNotificationConfigs(0, 10), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
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

    it("should invalidate notificationConfigs on success", async () => {
      vi.mocked(notificationService.saveNotificationConfig).mockResolvedValue(mockSuccess);
      vi.mocked(notificationService.getNotificationConfigs).mockResolvedValue(mockConfigsResponse);

      const wrapper = createWrapper();

      const { result: listResult } = renderHook(() => useGetNotificationConfigs(0, 10), {
        wrapper,
      });
      await waitFor(() => expect(listResult.current.isSuccess).toBe(true));

      const { result: saveResult } = renderHook(() => useSaveNotificationConfig(), { wrapper });
      saveResult.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(saveResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(notificationService.getNotificationConfigs).toHaveBeenCalledTimes(2);
      });
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
