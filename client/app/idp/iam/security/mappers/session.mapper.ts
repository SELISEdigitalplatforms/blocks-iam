import { tStatic } from "@seliseblocks/genesis-os/i18n";
import type {
  IApplicationApi,
  ISessionDetailsApi,
  ISessionOverviewApi,
  ITimelineEventApi,
  IUserSessionApi,
} from "../api";
import type {
  IApplicationViewModel,
  ISessionCardViewModel,
  ISessionDetailsViewModel,
  ISessionOverviewViewModel,
  ITimelineEventViewModel,
} from "../view-models/session.view-model";
import { formatAbsoluteWithSeconds, formatAbsoluteUtcWithSeconds, formatRelative } from "../utils/date-format";

const IAM_SECURITY_MODULE = "iam.security";

const statusLabel = (status: string): string => {
  const labels: Record<string, string> = {
    Active: tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.STATUS_ACTIVE"),
    Expired: tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.STATUS_EXPIRED"),
    Revoked: tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.STATUS_REVOKED"),
  };
  return labels[status] ?? status;
};

export const toSessionCardViewModel = (api: IUserSessionApi): ISessionCardViewModel => {
  const extraApps = Math.max(0, api.applicationCount - 1);
  return {
    id: api.sessionId,
    deviceName: api.primaryDeviceName ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_DEVICE"),
    browser: api.primaryBrowser ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_BROWSER"),
    operatingSystem: api.primaryOperatingSystem ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_OS"),
    ipAddress: api.primaryIpAddress ?? "—",
    lastActivityDisplay: formatRelative(api.lastActivityAt),
    expiresDisplay: formatAbsoluteWithSeconds(api.absoluteExpiry),
    status: api.status,
    isCurrent: api.isCurrent,
    applicationSummary:
      extraApps > 0
        ? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.MORE_APPS", { count: extraApps })
        : tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.APPS", { count: api.applicationCount }),
  };
};

export const toSessionOverviewViewModel = (
  api: ISessionOverviewApi,
): ISessionOverviewViewModel => ({
  sessionId: api.sessionId,
  statusLabel: statusLabel(api.status),
  deviceName: api.deviceName ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_DEVICE"),
  browser: api.browser ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_BROWSER"),
  operatingSystem: api.operatingSystem ?? tStatic(IAM_SECURITY_MODULE, "SESSION_MAPPER.UNKNOWN_OS"),
  ipAddress: api.ipAddress ?? "—",
  startedAtDisplay: formatAbsoluteUtcWithSeconds(api.startedAt),
  lastActivityAtDisplay: formatRelative(api.lastActivityAt),
  absoluteExpiryDisplay: formatAbsoluteUtcWithSeconds(api.absoluteExpiry),
  idleExpiryDisplay: formatAbsoluteUtcWithSeconds(api.idleExpiry),
  isCurrent: api.isCurrent,
});

export const toApplicationViewModel = (api: IApplicationApi): IApplicationViewModel => ({
  clientName: api.clientName ?? api.clientId,
  statusLabel: statusLabel(api.status),
  expiresDisplay: formatAbsoluteUtcWithSeconds(api.expiresAt),
  lastRotationDisplay: api.lastRotationAt ? formatAbsoluteUtcWithSeconds(api.lastRotationAt) : "—",
  rotationCountLabel: `${api.rotationCount}`,
  revokeReason: api.revokeReason ?? undefined,
});

const TONE_MAP: Record<string, "info" | "warn" | "danger" | "success"> = {
  Auth: "info",
  Refresh: "info",
  Revocation: "warn",
};

export const toTimelineEventViewModel = (api: ITimelineEventApi): ITimelineEventViewModel => ({
  type: api.type,
  label: api.event ?? api.type,
  timestampDisplay: formatAbsoluteUtcWithSeconds(api.at),
  secondary: [
    api.outcome,
    api.reasonCode,
    api.clientId,
  ]
    .filter((v): v is string => !!v && v.length > 0)
    .join(" • ") || undefined,
  tone: TONE_MAP[api.type] ?? "info",
});

export const toSessionDetailsViewModel = (api: ISessionDetailsApi): ISessionDetailsViewModel | null => {
  if (!api.overview) return null;
  return {
    overview: toSessionOverviewViewModel(api.overview),
    applications: api.applications.map(toApplicationViewModel),
    timeline: api.timeline.map(toTimelineEventViewModel),
  };
};