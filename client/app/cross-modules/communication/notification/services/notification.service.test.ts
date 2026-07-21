import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { NotificationService } from "./notification.service";
import { NOTIFICATION_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const http = serviceInstances.idpService;

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
  projectKey: TEST_PROJECT_KEY,
  isUpdateRequest: false,
};

describe("NotificationService (communication)", () => {
  let service: NotificationService;

  beforeEach(() => {
    service = new NotificationService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getNotificationConfigs ────────────────────────────────────────────────
  describe("getNotificationConfigs", () => {
    it("should GET the configs endpoint with pagination and project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockConfigsResponse);

      const result = await service.getNotificationConfigs(0, 10, TEST_PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?page=0&pageSize=10&projectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockConfigsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getNotificationConfigs(0, 10, TEST_PROJECT_KEY)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveNotificationConfig ────────────────────────────────────────────────
  describe("saveNotificationConfig", () => {
    it("should POST the payload to the save endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccess);

      const result = await service.saveNotificationConfig(mockSaveConfigPayload);

      expect(http.post).toHaveBeenCalledWith(
        NOTIFICATION_CONFIG_ENDPOINTS.SAVE_CONFIG,
        mockSaveConfigPayload,
      );
      expect(result).toEqual(mockSuccess);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveNotificationConfig(mockSaveConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── deleteNotificationConfig ──────────────────────────────────────────────
  describe("deleteNotificationConfig", () => {
    it("should DELETE the endpoint with item id and project key", async () => {
      vi.mocked(http.delete).mockResolvedValue(mockSuccess);

      const result = await service.deleteNotificationConfig({
        itemId: "cfg-1",
        projectKey: TEST_PROJECT_KEY,
      });

      expect(http.delete).toHaveBeenCalledWith(
        `${NOTIFICATION_CONFIG_ENDPOINTS.DELETE_CONFIG}?itemId=cfg-1&projectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockSuccess);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.delete).mockRejectedValue(new Error("Network error"));

      await expect(
        service.deleteNotificationConfig({ itemId: "cfg-1", projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });
});
