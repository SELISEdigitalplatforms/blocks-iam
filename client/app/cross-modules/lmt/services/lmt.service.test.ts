import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { lmtService } from "./lmt.service";
import { LogService } from "./log.service";
import { TraceService } from "./trace.service";
import { UsageService } from "./usage.service";
import { LOG_ENDPOINTS, TRACE_ENDPOINTS } from "../constants/endpoint.constant";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockLogsResponse = { data: [], errors: null, totalCount: 0 };
const mockGetLogsPayload = { serviceName: "iam", page: 0, pageSize: 20, projectKey: "project-1" };

const mockUsageResponse = [{ _id: "iam-api", TotalRequests: 5 }];
const mockServiceAnalyticsPayload = {
  serviceName: "iam",
  startTime: "2025-01-01T00:00:00Z",
  endTime: "2025-01-01T01:00:00Z",
};

describe("lmtService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("should compose log, trace and usage services", () => {
    expect(lmtService.log).toBeInstanceOf(LogService);
    expect(lmtService.trace).toBeInstanceOf(TraceService);
    expect(lmtService.usage).toBeInstanceOf(UsageService);
  });

  describe("log.getLogs", () => {
    it("should POST the payload to the logs endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockLogsResponse);

      const result = await lmtService.log.getLogs(mockGetLogsPayload);

      expect(http.post).toHaveBeenCalledWith(LOG_ENDPOINTS.GET_LOGS, mockGetLogsPayload);
      expect(result).toEqual(mockLogsResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(lmtService.log.getLogs(mockGetLogsPayload)).rejects.toThrow("Network error");
    });
  });

  describe("usage.getServiceAnalytics", () => {
    it("should POST the payload to the service-analytics endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUsageResponse);

      const result = await lmtService.usage.getServiceAnalytics(mockServiceAnalyticsPayload);

      expect(http.post).toHaveBeenCalledWith(
        TRACE_ENDPOINTS.GET_SERVICE_ANALYTICS,
        mockServiceAnalyticsPayload,
      );
      expect(result).toEqual(mockUsageResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        lmtService.usage.getServiceAnalytics(mockServiceAnalyticsPayload),
      ).rejects.toThrow("Network error");
    });
  });
});
