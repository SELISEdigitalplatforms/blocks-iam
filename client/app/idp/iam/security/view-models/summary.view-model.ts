export interface ISecuritySummaryViewModel {
  currentSessionBadge: boolean;
  totals: {
    active: number;
    expired: number;
    revoked: number;
  };
  lastActivityDisplay: string;
  lastLoginDisplay: string;
}