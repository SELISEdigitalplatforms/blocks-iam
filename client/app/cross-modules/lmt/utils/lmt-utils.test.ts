import { describe, expect, it } from "vitest";
import { LOG_LEVEL, getLogFormatTimestamp, getLogLevelClassName } from "./index";
import {
  abbreviateNumber,
  abbreviateDurationMs,
  abbreviateBytes,
  transformMatrixData,
  defaultUsagesMetrics,
  getNormalizeUsageMetricsData,
} from "./usage.util";
import { QUOTA_REDIRECT_CONFIG } from "./quota-redirect-config";

describe("lmt utils/index", () => {
  describe("LOG_LEVEL", () => {
    it("should expose the log level labels", () => {
      expect(LOG_LEVEL).toEqual({
        Information: "Information",
        Warning: "Warning",
        Error: "Error",
      });
    });
  });

  describe("getLogFormatTimestamp", () => {
    it("should format a valid ISO timestamp to a trimmed 'YYYY-MM-DD HH:mm:ss' string", () => {
      expect(getLogFormatTimestamp("2024-01-15T10:30:45.123Z")).toBe("2024-01-15 10:30:45");
    });

    it("should return the raw input when it is not a valid date", () => {
      expect(getLogFormatTimestamp("not-a-date")).toBe("not-a-date");
    });
  });

  describe("getLogLevelClassName", () => {
    it("should map known levels to their class names", () => {
      expect(getLogLevelClassName("Warning")).toBe("text-warning");
      expect(getLogLevelClassName("Information")).toBe("text-success");
      expect(getLogLevelClassName("Error")).toBe("text-error");
    });

    it("should fall back to the high-emphasis class for unknown levels", () => {
      expect(getLogLevelClassName("Trace")).toBe("text-high-emphasis");
    });
  });
});

describe("lmt utils/usage.util", () => {
  describe("abbreviateNumber", () => {
    it("should keep values under 1000 as-is", () => {
      expect(abbreviateNumber(0)).toBe("0");
      expect(abbreviateNumber(999)).toBe("999");
    });

    it("should abbreviate thousands, millions and billions", () => {
      expect(abbreviateNumber(1000)).toBe("1k");
      expect(abbreviateNumber(1500)).toBe("1.5k");
      expect(abbreviateNumber(1_000_000)).toBe("1M");
      expect(abbreviateNumber(2_500_000_000)).toBe("2.5B");
    });
  });

  describe("abbreviateDurationMs", () => {
    it("should format sub-minute durations in seconds", () => {
      expect(abbreviateDurationMs(500)).toBe("0.50s");
    });

    it("should format minute-scale durations", () => {
      expect(abbreviateDurationMs(65000)).toBe("1m 5.00s");
    });

    it("should format hour-scale durations", () => {
      expect(abbreviateDurationMs(3_665_000)).toBe("1h 1m 5.00s");
    });
  });

  describe("abbreviateBytes", () => {
    it("should keep values under 1024 in bytes", () => {
      expect(abbreviateBytes(500)).toBe("500B");
    });

    it("should abbreviate kilobytes and megabytes", () => {
      expect(abbreviateBytes(1024)).toBe("1K");
      expect(abbreviateBytes(2048)).toBe("2K");
      expect(abbreviateBytes(1_048_576)).toBe("1M");
    });
  });

  describe("transformMatrixData", () => {
    it("should coerce string numbers, keep _id as a string, and default missing fields to 0", () => {
      const result = transformMatrixData({
        _id: 42 as unknown as string,
        TotalRequests: "10" as unknown as number,
        Status2xx: "3" as unknown as number,
        Status4xx: "abc" as unknown as number,
      });

      expect(result._id).toBe("42");
      expect(result.TotalRequests).toBe(10);
      expect(result.Status2xx).toBe(3);
      expect(result.Status4xx).toBe(0);
      expect(result.Status5xx).toBe(0);
      expect(result.TotalDuration).toBe(0);
    });
  });

  describe("defaultUsagesMetrics", () => {
    it("should be an all-zero summary", () => {
      expect(defaultUsagesMetrics.TotalRequests).toBe(0);
      expect(defaultUsagesMetrics.successRate).toBe(0);
      expect(defaultUsagesMetrics.errorRate).toBe(0);
    });
  });

  describe("getNormalizeUsageMetricsData", () => {
    it("should accumulate zeros for empty data and preserve the endTime", () => {
      const payload = {
        startTime: "2025-01-01T00:00:00Z",
        endTime: "2025-01-01T01:00:00Z",
      } as never;

      const result = getNormalizeUsageMetricsData([], payload);

      expect(result.accumulatedApiCall).toBe(0);
      expect(result.accumulatedError).toBe(0);
      expect(result.accumulatedSuccess).toBe(0);
      expect(result.accumulatedAverageDuration).toBe(0);
      expect(result.endTime).toBe("2025-01-01T01:00:00Z");
      expect(typeof result.services).toBe("object");
    });
  });
});

describe("lmt utils/quota-redirect-config", () => {
  it("should map quota keys to redirect paths", () => {
    expect(QUOTA_REDIRECT_CONFIG.PEOPLE).toBe("/people");
    expect(QUOTA_REDIRECT_CONFIG.IAM).toBe("/app/iam");
  });
});
