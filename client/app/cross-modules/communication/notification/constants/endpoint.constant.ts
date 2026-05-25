export const NOTIFICATION_ENDPOINTS = {
  GET_NOTIFICATIONS: `/api/Notifier/GetNotifications`,
  MARK_AS_READ: `/api/Notifier/MarkNotificationAsRead`,
  MARK_ALL_AS_READ: `/api/Notifier/MarkAllNotificationAsRead`,
} as const;

export const NOTIFICATION_CONFIG_ENDPOINTS = {
  GET_CONFIGS: `/api/Notification/Gets`,
  SAVE_CONFIG: `/api/Notification/Save`,
  DELETE_CONFIG: `/api/Notification/Delete`,
} as const;
