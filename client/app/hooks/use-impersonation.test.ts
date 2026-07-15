import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { impersonationService } from "@/services/impersonation.service";
import {
  useStartImpersonation,
  useStopImpersonation,
  useImpersonationStatusChecker,
} from "./use-impersonation";

vi.mock("@/services/impersonation.service", () => ({
  impersonationService: {
    startImpersonation: vi.fn(),
    stopImpersonation: vi.fn(),
    impersonationStatus: vi.fn(),
  },
}));

const mockState = {
  rootTenantId: "tenant-1",
  targetTenantId: "tenant-2",
  orgId: "org-1",
  startedAtUtc: "2025-01-01T00:00:00Z",
};

const mockStatus = {
  impersonated: true,
  originalTenantId: "tenant-1",
  impersonatedTenantId: "tenant-2",
};

describe("Impersonation Hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ─── useStartImpersonation ─────────────────────────────────────────────────
  describe("useStartImpersonation", () => {
    it("should start impersonation with the request", async () => {
      vi.mocked(impersonationService.startImpersonation).mockResolvedValue(mockState);

      const request = { targeted_tenant_id: "tenant-2", organizationId: "org-1" };
      const { result } = renderHook(() => useStartImpersonation(), { wrapper: createWrapper() });

      result.current.mutate(request);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(impersonationService.startImpersonation).toHaveBeenCalledWith(
        request,
        expect.anything(),
      );
      expect(result.current.data).toEqual(mockState);
    });

    it("should surface errors", async () => {
      vi.mocked(impersonationService.startImpersonation).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useStartImpersonation(), { wrapper: createWrapper() });

      result.current.mutate({ targeted_tenant_id: "tenant-2" });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useStopImpersonation ──────────────────────────────────────────────────
  describe("useStopImpersonation", () => {
    it("should stop impersonation", async () => {
      vi.mocked(impersonationService.stopImpersonation).mockResolvedValue(undefined);

      const { result } = renderHook(() => useStopImpersonation(), { wrapper: createWrapper() });

      result.current.mutate();

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(impersonationService.stopImpersonation).toHaveBeenCalled();
    });
  });

  // ─── useImpersonationStatusChecker ─────────────────────────────────────────
  describe("useImpersonationStatusChecker", () => {
    it("should query the impersonation status", async () => {
      vi.mocked(impersonationService.impersonationStatus).mockResolvedValue(mockStatus);

      const { result } = renderHook(() => useImpersonationStatusChecker(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockStatus);
      expect(impersonationService.impersonationStatus).toHaveBeenCalled();
    });

    it("should surface errors", async () => {
      vi.mocked(impersonationService.impersonationStatus).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useImpersonationStatusChecker(), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
