const SECURITY_SUBPATH = "/security";

export const SECURITY_ENDPOINTS = {
  SUMMARY: `/api${SECURITY_SUBPATH}/summary`,
  SESSIONS: `/api${SECURITY_SUBPATH}/sessions`,
  SESSION_DETAILS: `/api${SECURITY_SUBPATH}/sessions/{sessionId}`,
  REVOKE_SESSION: `/api${SECURITY_SUBPATH}/sessions/{sessionId}/revoke`,
  REVOKE_REFRESH_TOKEN: `/api${SECURITY_SUBPATH}/revoke/refresh-tokens/{tokenId}`,
  ACTIVITY: `/api${SECURITY_SUBPATH}/activity`,
} as const;