// ─── Log endpoints ────────────────────────────────────────────────────────────

const LOG_SUBPATH = "/Log";

export const LOG_ENDPOINTS = {
  GET_LOGS: `/api${LOG_SUBPATH}/GetLogs`,
  GET_LOGS_BY_DATE: `/api${LOG_SUBPATH}/GetLogsByDate`,
  LIVE: `/api${LOG_SUBPATH}/Live`,
} as const;

// ─── Trace endpoints ──────────────────────────────────────────────────────────

const TRACE_SUBPATH = "/Trace";

export const TRACE_ENDPOINTS = {
  GET_TRACES: `/api${TRACE_SUBPATH}/GetTraces`,
  GET_TRACE: `/api${TRACE_SUBPATH}/GetTrace`,
  GET_OPERATIONAL_ANALYTICS: `/api${TRACE_SUBPATH}/GetOperationalAnalytics`,
  GET_SERVICE_ANALYTICS: `/api${TRACE_SUBPATH}/GetServiceAnalytics`,
} as const;
