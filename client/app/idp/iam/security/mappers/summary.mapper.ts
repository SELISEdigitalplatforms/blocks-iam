import type { ISecuritySummaryApi } from "../api";
import type { ISecuritySummaryViewModel } from "../view-models/summary.view-model";
import { formatRelative } from "../utils/date-format";

export const toSecuritySummaryViewModel = (
  api: ISecuritySummaryApi,
): ISecuritySummaryViewModel => ({
  currentSessionBadge: !!api.currentSessionId,
  totals: {
    active: api.activeSessions,
    expired: api.expiredSessions,
    revoked: api.revokedSessions,
  },
  lastActivityDisplay: formatRelative(api.lastActivityAt),
  lastLoginDisplay: formatRelative(api.lastLoginAt),
});