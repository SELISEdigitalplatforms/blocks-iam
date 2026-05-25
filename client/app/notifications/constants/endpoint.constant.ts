const NOTIFIER_BASE = "/api/Notifier";

export const NOTIFICATION_ENDPOINTS = {
  GET_NOTIFICATIONS: `${NOTIFIER_BASE}/GetNotifications`,
  MARK_AS_READ: `${NOTIFIER_BASE}/MarkNotificationAsRead`,
  MARK_ALL_AS_READ: `${NOTIFIER_BASE}/MarkAllNotificationAsRead`,
} as const;

export const NOTIFICATION_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api/Notification/Gets`,
  SAVE_CONFIG: `/api/Notification/Save`,
  DELETE_CONFIG: `/api/Notification/Delete`,
};
