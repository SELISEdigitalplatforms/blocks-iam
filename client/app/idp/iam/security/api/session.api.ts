export type SessionStatus = "Active" | "Expired" | "Revoked";

export type TimelineEventType = "Auth" | "Refresh" | "Revocation";

export interface IUserSessionApi {
  sessionId: string;
  tenantId: string;
  userId?: string | null;
  createdAt: string;
  lastActivityAt: string;
  absoluteExpiry: string;
  idleExpiry: string;
  isCurrent: boolean;
  status: SessionStatus;
  primaryDeviceName?: string | null;
  primaryOperatingSystem?: string | null;
  primaryBrowser?: string | null;
  primaryIpAddress?: string | null;
  applicationCount: number;
  clientIds: string[];
}

export interface ISessionOverviewApi {
  sessionId: string;
  status: SessionStatus;
  isCurrent: boolean;
  deviceName?: string | null;
  deviceModel?: string | null;
  operatingSystem?: string | null;
  browser?: string | null;
  ipAddress?: string | null;
  location?: string | null;
  clientIds: string[];
  organizationIds: string[];
  startedAt: string;
  lastActivityAt: string;
  absoluteExpiry: string;
  idleExpiry: string;
}

export interface IApplicationApi {
  clientId: string;
  clientName?: string | null;
  organizationId?: string | null;
  grantType?: string | null;
  status: SessionStatus;
  expiresAt: string;
  lastRotationAt?: string | null;
  rotationCount: number;
  revokedAt?: string | null;
  revokeReason?: string | null;
}

export interface ITimelineEventApi {
  type: TimelineEventType;
  at: string;
  event?: string | null;
  outcome?: string | null;
  reasonCode?: string | null;
  ipAddress?: string | null;
  deviceName?: string | null;
  clientId?: string | null;
}

export interface ISessionDetailsApi {
  overview?: ISessionOverviewApi | null;
  applications: IApplicationApi[];
  timeline: ITimelineEventApi[];
}

export interface IRevokeSessionResponseApi {
  sessionId?: string | null;
  alreadyRevoked: boolean;
  revokedAt?: string | null;
  reason?: string | null;
  revokedRefreshTokens: number;
  clientId?: string | null;
  warnings: string[];
}