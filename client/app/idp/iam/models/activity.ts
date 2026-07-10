export type UserActivityCategory = "Account" | "Auth" | "Resource" | "Audit";

export interface IDeviceInformation {
  browser?: string;
  os?: string;
  device?: string;
  brand?: string;
  model?: string;
}

export interface IActivityContext {
  ipAddress?: string;
  deviceName?: string;
  deviceType?: string;
  userAgent?: string;
  deviceInformation?: IDeviceInformation;
}

export interface IUserActivity {
  itemId: string;
  userId: string;
  actorUserId: string;
  category: UserActivityCategory;
  event: string;
  outcome?: "Success" | "Failure" | "Blocked" | string;
  reasonCode?: string;
  severity?: "Info" | "Warn" | "Critical" | string;
  source?: string;
  messageId?: string;
  correlationId?: string;
  sessionId?: string;
  clientId?: string;
  tenantId?: string;
  context?: IActivityContext;
  entity?: string;
  entityId?: string;
  metadata?: Record<string, string>;
  createdDate: string;
}

export interface IActivityFilter {
  userId?: string;
  actorUserId?: string;
  categories?: UserActivityCategory[];
  events?: string[];
  outcomes?: string[];
  severities?: string[];
  source?: string;
  sessionId?: string;
  clientId?: string;
  tenantId?: string;
  organizationId?: string;
  correlationId?: string;
  entity?: string;
  entityId?: string;
  from?: string;
  to?: string;
  search?: string;
}

export interface IGetActivitiesPayload {
  userId: string;
  page: number;
  pageSize: number;
  sort?: { property: string; isDescending: boolean };
  filter?: IActivityFilter;
}

export interface IUserActivityResponse {
  data: IUserActivity[];
  totalCount: number;
  errors: unknown;
}
