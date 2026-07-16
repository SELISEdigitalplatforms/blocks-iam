import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { deviceService } from "./device.service";
import { DEVICE_ENDPOINTS } from "../constants/endpoints/device.endpoint";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockVerifyResponse = {
  status: "ready",
  payload: {
    clientName: "Test App",
    clientId: "client-1",
    scopes: ["openid"],
    tenant: "tenant-1",
    userCode: "ABCD-1234",
  },
};

const mockApproveResponse = { redirect: "https://example.com/done", status: "Approved" };

describe("deviceService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── verify ────────────────────────────────────────────────────────────────
  describe("verify", () => {
    it("should POST the user_code with the tenant header", async () => {
      vi.mocked(http.post).mockResolvedValue(mockVerifyResponse);

      const result = await deviceService.verify("ABCD-1234", "tenant-1");

      expect(http.post).toHaveBeenCalledWith(
        DEVICE_ENDPOINTS.VERIFY,
        { user_code: "ABCD-1234" },
        { "X-Blocks-Key": "tenant-1" },
      );
      expect(result).toEqual(mockVerifyResponse);
    });

    it("should omit the tenant header when tenantId is empty", async () => {
      vi.mocked(http.post).mockResolvedValue(mockVerifyResponse);

      await deviceService.verify("ABCD-1234", "");

      expect(http.post).toHaveBeenCalledWith(
        DEVICE_ENDPOINTS.VERIFY,
        { user_code: "ABCD-1234" },
        undefined,
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(deviceService.verify("ABCD-1234", "tenant-1")).rejects.toThrow("Network error");
    });
  });

  // ─── decide ────────────────────────────────────────────────────────────────
  describe("decide", () => {
    it("should POST the decision with the tenant header", async () => {
      vi.mocked(http.post).mockResolvedValue(mockApproveResponse);

      const result = await deviceService.decide("ABCD-1234", "allow", "tenant-1");

      expect(http.post).toHaveBeenCalledWith(
        DEVICE_ENDPOINTS.DECISION,
        { user_code: "ABCD-1234", decision: "allow" },
        { "X-Blocks-Key": "tenant-1" },
      );
      expect(result).toEqual(mockApproveResponse);
    });

    it("should forward a deny decision", async () => {
      vi.mocked(http.post).mockResolvedValue({ ...mockApproveResponse, status: "Denied" });

      await deviceService.decide("ABCD-1234", "deny", "tenant-1");

      expect(http.post).toHaveBeenCalledWith(
        DEVICE_ENDPOINTS.DECISION,
        { user_code: "ABCD-1234", decision: "deny" },
        { "X-Blocks-Key": "tenant-1" },
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(deviceService.decide("ABCD-1234", "allow", "tenant-1")).rejects.toThrow(
        "Network error",
      );
    });
  });
});
