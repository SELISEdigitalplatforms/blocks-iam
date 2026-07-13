export const DEVICE_ENDPOINTS = {
  BEGIN: "/api/device",
  continue: (interactionId: string) =>
    `/api/device/continue/${encodeURIComponent(interactionId)}`,
  APPROVE: "/api/device/approve",
} as const;
