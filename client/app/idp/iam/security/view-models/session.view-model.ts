import type { SessionStatus, TimelineEventType } from "../api";

export interface ISessionCardViewModel {
  id: string;
  deviceName: string;
  browser: string;
  operatingSystem: string;
  ipAddress: string;
  lastActivityDisplay: string;
  expiresDisplay: string;
  status: SessionStatus;
  isCurrent: boolean;
  applicationSummary: string;
}

export interface IApplicationViewModel {
  clientName: string;
  statusLabel: string;
  expiresDisplay: string;
  lastRotationDisplay: string;
  rotationCountLabel: string;
  revokeReason?: string;
}

export interface ITimelineEventViewModel {
  type: TimelineEventType;
  label: string;
  timestampDisplay: string;
  secondary?: string;
  tone: "info" | "warn" | "danger" | "success";
}

export interface ISessionOverviewViewModel {
  sessionId: string;
  statusLabel: string;
  deviceName: string;
  browser: string;
  operatingSystem: string;
  ipAddress: string;
  startedAtDisplay: string;
  lastActivityAtDisplay: string;
  absoluteExpiryDisplay: string;
  idleExpiryDisplay: string;
  isCurrent: boolean;
}

export interface ISessionDetailsViewModel {
  overview: ISessionOverviewViewModel;
  applications: IApplicationViewModel[];
  timeline: ITimelineEventViewModel[];
}