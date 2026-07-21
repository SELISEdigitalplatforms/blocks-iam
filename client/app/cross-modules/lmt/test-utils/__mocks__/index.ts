import { vi } from "vitest";
import type { IGetLogsPayload, IGetLogsByDatePayload } from "../../models/log.model";
import type {
  IGetTracesPayload,
  IGetTraceByTraceIdPayload,
  Trace,
} from "../../models/trace.model";
import type {
  IGetOperationalAnalyticsPayload,
  IGetServiceAnalyticsPayload,
  UsageMatrix,
} from "../../models/usage.model";

// ─── Log mock data ────────────────────────────────────────────────────────────

export const mockLogsResponse = {
  data: [
    {
      timestamp: "2026-01-15T10:00:00.000Z",
      level: "Information",
      message: "Request received at /api/users",
      traceId: "trace-abc-001",
    },
    {
      timestamp: "2026-01-15T10:00:01.000Z",
      level: "Warning",
      message: "Slow query detected on user lookup",
      traceId: "trace-abc-002",
    },
  ],
  errors: [],
  totalCount: 2,
};

export const mockEmptyLogsResponse = {
  data: [],
  errors: [],
  totalCount: 0,
};

export const mockGetLogsPayload: IGetLogsPayload = {
  page: 1,
  pageSize: 20,
  sort: {
    property: "timestamp",
    isDescending: true,
  },
  filter: {
    startDate: "2026-01-15T00:00:00.000Z",
    endDate: "2026-01-15T23:59:59.999Z",
    level: "Information",
  },
  search: "",
  serviceName: "blocks-idp-api",
  projectKey: "test-project-key-123",
};

export const mockGetLogsByDatePayload: IGetLogsByDatePayload = {
  pageSize: 20,
  sort: {
    property: "timestamp",
    isDescending: true,
  },
  filter: {
    startDate: "2026-01-15T00:00:00.000Z",
    endDate: "2026-01-15T10:00:00.000Z",
  },
  search: "",
  serviceName: "blocks-idp-api",
};

// ─── Trace mock data ──────────────────────────────────────────────────────────

export const mockTrace1: Trace = {
  timestamp: "2026-01-15T10:00:00.000Z",
  traceId: "trace-001",
  spanId: "span-001",
  parentSpanId: "",
  parentId: "",
  kind: "Server",
  activitySourceName: "blocks-idp-api",
  operationName: "GET /api/users",
  startTime: "2026-01-15T10:00:00.000Z",
  endTime: "2026-01-15T10:00:00.200Z",
  duration: 200,
  attributes: { "http.status_code": 200 },
  status: "Ok",
  statusDescription: "",
  baggage: {
    TenantId: "test-tenant-id-123",
    IsFromCloud: "true",
  },
  serviceName: "blocks-idp-api",
};

export const mockTrace2: Trace = {
  timestamp: "2026-01-15T10:00:00.050Z",
  traceId: "trace-001",
  spanId: "span-002",
  parentSpanId: "span-001",
  parentId: "span-001",
  kind: "Client",
  activitySourceName: "blocks-idp-api",
  operationName: "POST /api/auth",
  startTime: "2026-01-15T10:00:00.050Z",
  endTime: "2026-01-15T10:00:00.150Z",
  duration: 100,
  attributes: { "http.status_code": 200 },
  status: "Ok",
  statusDescription: "",
  baggage: {
    TenantId: "test-tenant-id-123",
    IsFromCloud: "true",
  },
  serviceName: "blocks-idp-api",
};

export const mockTracesApiResponse = {
  data: [mockTrace1],
  errors: [],
  totalCount: 1,
};

export const mockTraceByIdApiResponse = {
  data: [mockTrace1, mockTrace2],
  errors: [],
  totalCount: 0,
};

export const mockGetTracesPayload: IGetTracesPayload = {
  page: 1,
  pageSize: 20,
  sort: {
    property: "timestamp",
    isDescending: true,
  },
  filter: {
    startDate: "2026-01-15T00:00:00.000Z",
    endDate: "2026-01-15T23:59:59.999Z",
    services: [],
    excepts: [],
  },
  search: "",
  projectKey: "test-project-key-123",
};

export const mockGetTraceByIdPayload: IGetTraceByTraceIdPayload = {
  traceId: "trace-001",
};

// ─── Usage mock data ──────────────────────────────────────────────────────────

export const mockUsageMatrixResponse: UsageMatrix[] = [
  {
    _id: "usage-001",
    TotalRequests: 1500,
    Status1xx: 0,
    Status2xx: 1400,
    Status3xx: 20,
    Status4xx: 60,
    Status5xx: 20,
    TotalDuration: 45000,
    AverageDuration: 30,
    PeakDuration: 350,
    AverageThroughput: 2.5,
    TotalThroughput: 750,
  },
];

export const mockGetOperationalAnalyticsPayload: IGetOperationalAnalyticsPayload = {
  startTime: "2026-01-15T00:00:00.000Z",
  endTime: "2026-01-15T23:59:59.999Z",
  serviceName: "blocks-idp-api",
  projectKey: "test-project-key-123",
};

export const mockGetServiceAnalyticsPayload: IGetServiceAnalyticsPayload = {
  startTime: "2026-01-15T00:00:00.000Z",
  endTime: "2026-01-15T23:59:59.999Z",
  projectKey: "test-project-key-123",
};

// ─── Service factory ──────────────────────────────────────────────────────────

export const mockLmtServiceFactory = () => ({
  lmtService: {
    log: {
      getLogs: vi.fn(),
      getLogsByDate: vi.fn(),
      getLiveLog: vi.fn(),
    },
    trace: {
      getTraces: vi.fn(),
      getTraceByTraceId: vi.fn(),
    },
    usage: {
      getOperationalAnalytics: vi.fn(),
      getServiceAnalytics: vi.fn(),
      getUsagesMetrics: vi.fn(),
    },
  },
});
