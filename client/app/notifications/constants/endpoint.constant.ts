import { API_BASES } from "@/constants/endpoint.constant";

const NOTIFIER_BASE = "/Notifier";

export const NOTIFICATION_ENDPOINTS = {
  GET_NOTIFICATIONS: `${NOTIFIER_BASE}/GetNotifications`,
  MARK_AS_READ: `${NOTIFIER_BASE}/MarkNotificationAsRead`,
  MARK_ALL_AS_READ: `${NOTIFIER_BASE}/MarkAllNotificationAsRead`,
} as const;

export const NOTIFICATION_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `${API_BASES.LOGIC}/Notification/Gets`,
  SAVE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}/Notification/Save`,
  DELETE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}/Notification/Delete`,
};
