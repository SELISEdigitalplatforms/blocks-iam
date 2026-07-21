import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { serviceRegistryService } from "@blocks-identifier/services/service-registery.service";
import { useRegisterService, useGetAllServices } from "./use-services";

vi.mock("@blocks-identifier/services/service-registery.service", () => ({
  serviceRegistryService: {
    registerService: vi.fn(),
    getAllServices: vi.fn(),
  },
}));

const mockRegisterResponse = { isSuccess: true, errors: null, itemId: "service-1" };
const mockServicesResponse = {
  isSuccess: true,
  errors: null,
  data: [{ itemId: "service-1", name: "svc" }],
  totalCount: 1,
};

describe("Service Registry Hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ─── useRegisterService ────────────────────────────────────────────────────
  describe("useRegisterService", () => {
    it("should register a service", async () => {
      vi.mocked(serviceRegistryService.registerService).mockResolvedValue(mockRegisterResponse);

      const payload = { name: "svc", projectKey: "project-1" };
      const { result } = renderHook(() => useRegisterService(), { wrapper: createWrapper() });

      result.current.mutate(payload as never);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(serviceRegistryService.registerService).toHaveBeenCalledWith(payload);
    });

    it("should surface errors", async () => {
      vi.mocked(serviceRegistryService.registerService).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useRegisterService(), { wrapper: createWrapper() });

      result.current.mutate({ name: "svc" } as never);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetAllServices ─────────────────────────────────────────────────────
  describe("useGetAllServices", () => {
    it("should fetch services when a projectKey is provided", async () => {
      vi.mocked(serviceRegistryService.getAllServices).mockResolvedValue(mockServicesResponse);

      const options = { projectKey: "project-1", page: 0, pageSize: 20 };
      const { result } = renderHook(() => useGetAllServices(options), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockServicesResponse);
      expect(serviceRegistryService.getAllServices).toHaveBeenCalledWith(options);
    });

    it("should be disabled when projectKey is empty", async () => {
      vi.mocked(serviceRegistryService.getAllServices).mockResolvedValue(mockServicesResponse);

      const { result } = renderHook(
        () => useGetAllServices({ projectKey: "", page: 0, pageSize: 20 }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(serviceRegistryService.getAllServices).not.toHaveBeenCalled();
    });
  });
});
