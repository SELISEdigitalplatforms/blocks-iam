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

export interface IActivityItemApi {
  itemId: string;
  userId: string;
  actorUserId: string;
  category: UserActivityCategory;
  event: string;
  outcome?: string | null;
  reasonCode?: string | null;
  severity?: string | null;
  source?: string | null;
  correlationId?: string | null;
  sessionId?: string | null;
  clientId?: string | null;
  tenantId?: string | null;
  entity?: string | null;
  entityId?: string | null;
  context?: IActivityContext;
  createdDate: string;
}

export interface IActivityPageResponseApi {
  items: IActivityItemApi[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface IGetActivitiesPayload {
  userId?: string;
  page?: number;
  pageSize?: number;
  filter?: {
    sessionId?: string;
    clientId?: string;
    events?: string[];
    outcomes?: string[];
    categories?: UserActivityCategory[];
    from?: string;
    to?: string;
    search?: string;
  };
}