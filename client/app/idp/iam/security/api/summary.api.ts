export interface ISecuritySummaryApi {
  currentSessionId?: string | null;
  totalSessions: number;
  activeSessions: number;
  expiredSessions: number;
  revokedSessions: number;
  lastActivityAt?: string | null;
  lastLoginAt?: string | null;
}