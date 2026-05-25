import { http } from "@/lib/http-client";
import { INotification, INotificationConfig } from "../models/notification.model";
import {
  NOTIFICATION_ENDPOINTS,
  NOTIFICATION_CONFIG_ENDPOINTS,
} from "../constants/endpoint.constant";

export class NotificationService {
  getNotifications = (
    pageNumber: number,
    pageSize: number,
  ): Promise<{
    unReadNotificationsCount: number;
    totalNotificationsCount: number;
    notifications: INotification[];
  }> => {
    const params = new URLSearchParams({
      page: String(pageNumber - 1),
      pageSize: String(pageSize),
    });
    const url = `/api+${NOTIFICATION_ENDPOINTS.GET_NOTIFICATIONS}?${params}`;
    return http.get(url, undefined, { absoluteUrl: true });
  };

  markAsRead = (notificationId: string): Promise<{ errors: null | unknown; isSuccess: boolean }> => {
    return http.post(
      `/api+${NOTIFICATION_ENDPOINTS.MARK_AS_READ}`,
      { id: notificationId },
      undefined,
      { absoluteUrl: true },
    );
  };

  markAllNotificationsAsRead = (): Promise<{ errors: null | unknown; isSuccess: boolean }> => {
    return http.post(
      `/api+${NOTIFICATION_ENDPOINTS.MARK_ALL_AS_READ}`,
      {},
      undefined,
      { absoluteUrl: true },
    );
  };

  getNotificationConfig = (config: INotificationConfig, message: string): void => {
    let parsedMessage: unknown = message;
    if (typeof message === "string") {
      try {
        parsedMessage = JSON.parse(message);
      } catch {
        parsedMessage = message;
      }
    }
    const notificationEvent = new CustomEvent(config.notifyMethod, {
      detail: {
        method: config.notifyMethod,
        message: parsedMessage,
        timestamp: new Date().toISOString(),
        config: config,
      },
    });
    window.dispatchEvent(notificationEvent);
  };

  getNotificationConfigs = (
    page: number = 0,
    pageSize: number = 10,
    projectKey: string,
  ): Promise<{
    configurations: INotificationConfig[];
    totalCount: number;
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = `${NOTIFICATION_CONFIG_ENDPOINTS.GET_CONFIGS}?page=${page}&pageSize=${pageSize}&projectKey=${projectKey}`;
    return http.get(url, undefined, { absoluteUrl: true });
  };

  saveNotificationConfig = (payload: {
    name: string;
    channelToNotify: number;
    notificationType: number;
    enablePersistence: boolean;
    notifyMethod: string;
    projectKey: string;
    isUpdateRequest: boolean;
    itemId?: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    return http.post(NOTIFICATION_CONFIG_ENDPOINTS.SAVE_CONFIG, payload);
  };

  deleteNotificationConfig = (payload: {
    itemId: string;
    projectKey: string;
  }): Promise<{
    errors: null | unknown;
    isSuccess: boolean;
  }> => {
    const url = `${NOTIFICATION_CONFIG_ENDPOINTS.DELETE_CONFIG}?itemId=${payload.itemId}&projectKey=${payload.projectKey}`;
    return http.delete(url);
  };
}

export const notificationService = new NotificationService();
