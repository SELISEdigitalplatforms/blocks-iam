
export interface ILog {
  timestamp: string;
  level: string;
  message: string;
  traceId: string;
}

export interface IGetLogsPayload {
  page: number;
  pageSize: number;
  sort?: {
    property?: string;
    isDescending: boolean;
  };
  filter?: {
    startDate?: string;
    endDate?: string;
    level?: string;
    traceId?: string;
    spanId?: string;
  };
  search?: string;
  serviceName: string;
  projectKey: string;
}

export interface IGetLiveLogsPayload {
  serviceName: string;
  lastDate: string;
  projectKey: string;
}

export interface IGetLogsByDatePayload {
  page?: number;
  pageSize: number;
  sort?: {
    property?: string;
    isDescending: boolean;
  };
  filter?: {
    startDate?: string;
    endDate?: string;
    level?: string;
    traceId?: string;
    spanId?: string;
  };
  search?: string;
  serviceName: string;
  projectKey: string;
}
