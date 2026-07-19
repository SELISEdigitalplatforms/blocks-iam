import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { impersonationService } from "./impersonation.service";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const mockImpersonationRequest = { targeted_tenant_id: "tenant-2", organizationId: "org-1" };
const mockImpersonationState = {
  rootTenantId: "tenant-1",
  targetTenantId: "tenant-2",
  orgId: "org-1",
  startedAtUtc: "2025-01-01T00:00:00Z",
};
const mockStatusResponse = {
  impersonated: true,
  originalTenantId: "tenant-1",
  impersonatedTenantId: "tenant-2",
};

describe("impersonationService", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  describe("startImpersonation", () => {
    it("should POST the request to the impersonate endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockImpersonationState);

      const result = await impersonationService.startImpersonation(mockImpersonationRequest);

      expect(http.post).toHaveBeenCalledWith("/api/auth/impersonate", mockImpersonationRequest);
      expect(result).toEqual(mockImpersonationState);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        impersonationService.startImpersonation(mockImpersonationRequest),
      ).rejects.toThrow("Network error");
    });
  });

  describe("stopImpersonation", () => {
    it("should POST to the stop endpoint with an empty body", async () => {
      vi.mocked(http.post).mockResolvedValue(undefined);

      await impersonationService.stopImpersonation();

      expect(http.post).toHaveBeenCalledWith("/api/auth/impersonation/stop", {}, undefined);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(impersonationService.stopImpersonation()).rejects.toThrow("Network error");
    });
  });

  describe("impersonationStatus", () => {
    it("should POST to the status endpoint skipping token rotation", async () => {
      vi.mocked(http.post).mockResolvedValue(mockStatusResponse);

      const result = await impersonationService.impersonationStatus();

      expect(http.post).toHaveBeenCalledWith("/api/auth/impersonation/status", null, undefined, {
        skipTokenRotation: true,
      });
      expect(result).toEqual(mockStatusResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(impersonationService.impersonationStatus()).rejects.toThrow("Network error");
    });
  });
});
