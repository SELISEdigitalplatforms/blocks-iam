import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { NotificationService } from "./notification.service";
import {
  NOTIFICATION_ENDPOINTS,
  NOTIFICATION_CONFIG_ENDPOINTS,
} from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const http = serviceInstances.idpService;

// The vitest setup sets window.__BLOCKS_ENV__.BLOCKS_LOGIC_BASE_URL to this.
const LOGIC_BASE = "https://dev-logic.blocksdevelopers.com";

// ─── Inline mock data ────────────────────────────────────────────────────────
const mockNotificationsResponse = {
  unReadNotificationsCount: 2,
  totalNotificationsCount: 5,
  notifications: [],
};

const mockSuccess = { errors: null, isSuccess: true };

const mockConfigsResponse = {
  configurations: [],
  totalCount: 0,
  errors: null,
  isSuccess: true,
};

const mockNotifyConfig = {
  itemId: "cfg-1",
  name: "cfg",
  channelToNotify: 1,
  notificationType: 1,
  enablePersistence: true,
  notifyMethod: "onSomething",
};

describe("NotificationService (notifications)", () => {
  let service: NotificationService;

  beforeEach(() => {
    service = new NotificationService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getNotifications ──────────────────────────────────────────────────────
  describe("getNotifications", () => {
    it("should GET the absolute logic url with a page-1 offset and absoluteUrl option", async () => {
      vi.mocked(http.get).mockResolvedValue(mockNotificationsResponse);

      const result = await service.getNotifications(1, 10);

      expect(http.get).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_ENDPOINTS.GET_NOTIFICATIONS}?page=0&pageSize=10`,
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockNotificationsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getNotifications(1, 10)).rejects.toThrow("Network error");
    });
  });

  // ─── markAsRead ────────────────────────────────────────────────────────────
  describe("markAsRead", () => {
    it("should POST the notification id to the absolute mark-as-read url", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccess);

      const result = await service.markAsRead("notif-1");

      expect(http.post).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_ENDPOINTS.MARK_AS_READ}`,
        { id: "notif-1" },
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockSuccess);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.markAsRead("notif-1")).rejects.toThrow("Network error");
    });
  });

  // ─── markAllNotificationsAsRead ────────────────────────────────────────────
  describe("markAllNotificationsAsRead", () => {
    it("should POST an empty body to the absolute mark-all url", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccess);

      const result = await service.markAllNotificationsAsRead();

      expect(http.post).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_ENDPOINTS.MARK_ALL_AS_READ}`,
        {},
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockSuccess);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.markAllNotificationsAsRead()).rejects.toThrow("Network error");
    });
  });

  // ─── getNotificationConfig (window CustomEvent) ────────────────────────────
  describe("getNotificationConfig", () => {
    it("should dispatch a CustomEvent with a parsed message for valid JSON strings", () => {
      const dispatchSpy = vi.spyOn(window, "dispatchEvent");

      service.getNotificationConfig(mockNotifyConfig, JSON.stringify({ hello: "world" }));

      expect(dispatchSpy).toHaveBeenCalledTimes(1);
      const event = dispatchSpy.mock.calls[0][0] as CustomEvent;
      expect(event.type).toBe(mockNotifyConfig.notifyMethod);
      expect(event.detail.method).toBe(mockNotifyConfig.notifyMethod);
      expect(event.detail.message).toEqual({ hello: "world" });
      expect(event.detail.config).toEqual(mockNotifyConfig);

      dispatchSpy.mockRestore();
    });

    it("should dispatch a CustomEvent keeping the raw string for non-JSON messages", () => {
      const dispatchSpy = vi.spyOn(window, "dispatchEvent");

      service.getNotificationConfig(mockNotifyConfig, "not-json-at-all");

      expect(dispatchSpy).toHaveBeenCalledTimes(1);
      const event = dispatchSpy.mock.calls[0][0] as CustomEvent;
      expect(event.detail.message).toBe("not-json-at-all");

      dispatchSpy.mockRestore();
    });
  });

  // ─── getNotificationConfigs ────────────────────────────────────────────────
  describe("getNotificationConfigs", () => {
    it("should GET the absolute configs url with pagination and project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockConfigsResponse);

      const result = await service.getNotificationConfigs(0, 10, TEST_PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?page=0&pageSize=10&projectKey=${TEST_PROJECT_KEY}`,
        undefined,
        { absoluteUrl: true },
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
    const payload = {
      name: "cfg",
      channelToNotify: 1,
      notificationType: 1,
      enablePersistence: true,
      notifyMethod: "onSomething",
      projectKey: TEST_PROJECT_KEY,
      isUpdateRequest: false,
    };

    it("should POST the payload to the absolute save url", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccess);

      const result = await service.saveNotificationConfig(payload);

      expect(http.post).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_CONFIG_ENDPOINTS.SAVE_CONFIG}`,
        payload,
        undefined,
        { absoluteUrl: true },
      );
      expect(result).toEqual(mockSuccess);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveNotificationConfig(payload)).rejects.toThrow("Network error");
    });
  });

  // ─── deleteNotificationConfig ──────────────────────────────────────────────
  describe("deleteNotificationConfig", () => {
    it("should DELETE the absolute url with item id and project key", async () => {
      vi.mocked(http.delete).mockResolvedValue(mockSuccess);

      const result = await service.deleteNotificationConfig({
        itemId: "cfg-1",
        projectKey: TEST_PROJECT_KEY,
      });

      expect(http.delete).toHaveBeenCalledWith(
        `${LOGIC_BASE}${NOTIFICATION_CONFIG_ENDPOINTS.DELETE_CONFIG}?itemId=cfg-1&projectKey=${TEST_PROJECT_KEY}`,
        undefined,
        { absoluteUrl: true },
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
