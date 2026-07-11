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

const STATUS_LABEL: Record<string, string> = {
  Active: "Active",
  Expired: "Expired",
  Revoked: "Revoked",
};

export const toSessionCardViewModel = (api: IUserSessionApi): ISessionCardViewModel => {
  const extraApps = Math.max(0, api.applicationCount - 1);
  return {
    id: api.sessionId,
    deviceName: api.primaryDeviceName ?? "Unknown device",
    browser: api.primaryBrowser ?? "Unknown browser",
    operatingSystem: api.primaryOperatingSystem ?? "Unknown OS",
    ipAddress: api.primaryIpAddress ?? "—",
    lastActivityDisplay: formatRelative(api.lastActivityAt),
    expiresDisplay: formatAbsoluteWithSeconds(api.absoluteExpiry),
    status: api.status,
    isCurrent: api.isCurrent,
    applicationSummary:
      extraApps > 0
        ? `+${extraApps} more app${extraApps === 1 ? "" : "s"}`
        : `${api.applicationCount} app${api.applicationCount === 1 ? "" : "s"}`,
  };
};

export const toSessionOverviewViewModel = (
  api: ISessionOverviewApi,
): ISessionOverviewViewModel => ({
  sessionId: api.sessionId,
  statusLabel: STATUS_LABEL[api.status] ?? api.status,
  deviceName: api.deviceName ?? "Unknown device",
  browser: api.browser ?? "Unknown browser",
  operatingSystem: api.operatingSystem ?? "Unknown OS",
  ipAddress: api.ipAddress ?? "—",
  startedAtDisplay: formatAbsoluteUtcWithSeconds(api.startedAt),
  lastActivityAtDisplay: formatRelative(api.lastActivityAt),
  absoluteExpiryDisplay: formatAbsoluteUtcWithSeconds(api.absoluteExpiry),
  idleExpiryDisplay: formatAbsoluteUtcWithSeconds(api.idleExpiry),
  isCurrent: api.isCurrent,
});

export const toApplicationViewModel = (api: IApplicationApi): IApplicationViewModel => ({
  clientName: api.clientName ?? api.clientId,
  statusLabel: STATUS_LABEL[api.status] ?? api.status,
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