import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import { magicUrlService } from "@blocks-utilities/services/magic-url.service";
import {
  useGetMagicUrls,
  useGetMagicUrlById,
  useCreateMagicUrl,
  useSaveMagicUrlConfig,
  useGetMagicUrlConfig,
  useRemoveMagicUrl,
} from "./use-magic-url";

vi.mock("@blocks-utilities/services/magic-url.service", () => ({
  magicUrlService: {
    getMagicUrls: vi.fn(),
    getMagicUrl: vi.fn(),
    createMagicUrl: vi.fn(),
    saveMagicUrlConfig: vi.fn(),
    getMagicUrlConfig: vi.fn(),
    deactivateMagicLinks: vi.fn(),
  },
}));

// ─── Inline mock data ────────────────────────────────────────────────────────
const mockMagicUrl = {
  itemId: "link-1",
  uri: "https://example.com/action",
  usageLimit: 10,
  usageCount: 0,
  shortUri: "https://dev-short.seliseblocks.com/abc",
  createdAt: "2025-01-01T00:00:00Z",
};

const mockGetUrlsResponse = { data: [mockMagicUrl], errors: [], totalCount: 1 };

const mockConfigResponse = {
  isSuccess: true,
  configId: "config-1",
  wasCreated: true,
  config: {
    itemId: "config-1",
    contextName: "ctx",
    shortUrlBase: "https://dev-short.seliseblocks.com/",
    projectKey: TEST_PROJECT_KEY,
    createdAt: "2025-01-01T00:00:00Z",
    updatedAt: "2025-01-01T00:00:00Z",
  },
};

const mockCreatePayload = {
  type: 1,
  uri: "https://example.com/action",
  projectKey: TEST_PROJECT_KEY,
};

const mockSaveConfigPayload = {
  contextName: "ctx",
  shortUrlBase: "https://dev-short.seliseblocks.com/",
  projectKey: TEST_PROJECT_KEY,
};

describe("Magic URL Hooks", () => {
  beforeEach(() => {
    vi.resetAllMocks();
  });

  // ─── useGetMagicUrls ───────────────────────────────────────────────────────
  describe("useGetMagicUrls", () => {
    it("should fetch magic urls when projectKey is present", async () => {
      vi.mocked(magicUrlService.getMagicUrls).mockResolvedValue(mockGetUrlsResponse);

      const option = { page: 1, pageSize: 10, projectKey: TEST_PROJECT_KEY };
      const { result } = renderHook(() => useGetMagicUrls(option), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockGetUrlsResponse);
      expect(magicUrlService.getMagicUrls).toHaveBeenCalledWith(option);
    });

    it("should be disabled when projectKey is empty", async () => {
      vi.mocked(magicUrlService.getMagicUrls).mockResolvedValue(mockGetUrlsResponse);

      const { result } = renderHook(
        () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: "" }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(magicUrlService.getMagicUrls).not.toHaveBeenCalled();
    });

    it("should handle errors", async () => {
      vi.mocked(magicUrlService.getMagicUrls).mockRejectedValue(new Error("Fetch failed"));

      const { result } = renderHook(
        () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetMagicUrlById ────────────────────────────────────────────────────
  describe("useGetMagicUrlById", () => {
    it("should fetch a magic url when ItemId and projectKey are present", async () => {
      vi.mocked(magicUrlService.getMagicUrl).mockResolvedValue(mockMagicUrl);

      const option = { ItemId: "link-1", projectKey: TEST_PROJECT_KEY };
      const { result } = renderHook(() => useGetMagicUrlById(option), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockMagicUrl);
      expect(magicUrlService.getMagicUrl).toHaveBeenCalledWith(option);
    });

    it("should be disabled when ItemId is empty", async () => {
      vi.mocked(magicUrlService.getMagicUrl).mockResolvedValue(mockMagicUrl);

      const { result } = renderHook(
        () => useGetMagicUrlById({ ItemId: "", projectKey: TEST_PROJECT_KEY }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(magicUrlService.getMagicUrl).not.toHaveBeenCalled();
    });

    it("should be disabled when projectKey is empty", async () => {
      vi.mocked(magicUrlService.getMagicUrl).mockResolvedValue(mockMagicUrl);

      const { result } = renderHook(
        () => useGetMagicUrlById({ ItemId: "link-1", projectKey: "" }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(magicUrlService.getMagicUrl).not.toHaveBeenCalled();
    });
  });

  // ─── useCreateMagicUrl ─────────────────────────────────────────────────────
  describe("useCreateMagicUrl", () => {
    it("should create a magic url successfully", async () => {
      vi.mocked(magicUrlService.createMagicUrl).mockResolvedValue(mockMagicUrl);

      const { result } = renderHook(() => useCreateMagicUrl(), { wrapper: createWrapper() });

      result.current.mutate(mockCreatePayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(magicUrlService.createMagicUrl).toHaveBeenCalledWith(mockCreatePayload);
    });

    it("should invalidate magic-urls on success", async () => {
      vi.mocked(magicUrlService.createMagicUrl).mockResolvedValue(mockMagicUrl);
      vi.mocked(magicUrlService.getMagicUrls).mockResolvedValue(mockGetUrlsResponse);

      const wrapper = createWrapper();

      const { result: listResult } = renderHook(
        () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: TEST_PROJECT_KEY }),
        { wrapper },
      );
      await waitFor(() => expect(listResult.current.isSuccess).toBe(true));

      const { result: createResult } = renderHook(() => useCreateMagicUrl(), { wrapper });
      createResult.current.mutate(mockCreatePayload);

      await waitFor(() => expect(createResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(magicUrlService.getMagicUrls).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(magicUrlService.createMagicUrl).mockRejectedValue(new Error("Create failed"));

      const { result } = renderHook(() => useCreateMagicUrl(), { wrapper: createWrapper() });

      result.current.mutate(mockCreatePayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useSaveMagicUrlConfig ─────────────────────────────────────────────────
  describe("useSaveMagicUrlConfig", () => {
    it("should save a magic url config successfully", async () => {
      vi.mocked(magicUrlService.saveMagicUrlConfig).mockResolvedValue(mockConfigResponse);

      const { result } = renderHook(() => useSaveMagicUrlConfig(), { wrapper: createWrapper() });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(magicUrlService.saveMagicUrlConfig).toHaveBeenCalledWith(mockSaveConfigPayload);
    });

    it("should handle errors", async () => {
      vi.mocked(magicUrlService.saveMagicUrlConfig).mockRejectedValue(new Error("Save failed"));

      const { result } = renderHook(() => useSaveMagicUrlConfig(), { wrapper: createWrapper() });

      result.current.mutate(mockSaveConfigPayload);

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useGetMagicUrlConfig ──────────────────────────────────────────────────
  describe("useGetMagicUrlConfig", () => {
    it("should fetch the config when projectKey is present", async () => {
      vi.mocked(magicUrlService.getMagicUrlConfig).mockResolvedValue(mockConfigResponse);

      const { result } = renderHook(() => useGetMagicUrlConfig(TEST_PROJECT_KEY), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockConfigResponse);
      expect(magicUrlService.getMagicUrlConfig).toHaveBeenCalledWith(TEST_PROJECT_KEY);
    });

    it("should be disabled when projectKey is empty", async () => {
      vi.mocked(magicUrlService.getMagicUrlConfig).mockResolvedValue(mockConfigResponse);

      const { result } = renderHook(() => useGetMagicUrlConfig(""), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(magicUrlService.getMagicUrlConfig).not.toHaveBeenCalled();
    });

    it("should be disabled when options.enabled is false", async () => {
      vi.mocked(magicUrlService.getMagicUrlConfig).mockResolvedValue(mockConfigResponse);

      const { result } = renderHook(
        () => useGetMagicUrlConfig(TEST_PROJECT_KEY, { enabled: false }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(magicUrlService.getMagicUrlConfig).not.toHaveBeenCalled();
    });
  });

  // ─── useRemoveMagicUrl ─────────────────────────────────────────────────────
  describe("useRemoveMagicUrl", () => {
    it("should remove magic urls successfully", async () => {
      vi.mocked(magicUrlService.deactivateMagicLinks).mockResolvedValue(undefined);

      const { result } = renderHook(() => useRemoveMagicUrl(), { wrapper: createWrapper() });

      const payload = { linkIds: ["link-1"], projectKey: TEST_PROJECT_KEY };
      result.current.mutate(payload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(magicUrlService.deactivateMagicLinks).toHaveBeenCalledWith(payload);
    });

    it("should invalidate magic-urls on success", async () => {
      vi.mocked(magicUrlService.deactivateMagicLinks).mockResolvedValue(undefined);
      vi.mocked(magicUrlService.getMagicUrls).mockResolvedValue(mockGetUrlsResponse);

      const wrapper = createWrapper();

      const { result: listResult } = renderHook(
        () => useGetMagicUrls({ page: 1, pageSize: 10, projectKey: TEST_PROJECT_KEY }),
        { wrapper },
      );
      await waitFor(() => expect(listResult.current.isSuccess).toBe(true));

      const { result: removeResult } = renderHook(() => useRemoveMagicUrl(), { wrapper });
      removeResult.current.mutate({ linkIds: ["link-1"], projectKey: TEST_PROJECT_KEY });

      await waitFor(() => expect(removeResult.current.isSuccess).toBe(true));

      await waitFor(() => {
        expect(magicUrlService.getMagicUrls).toHaveBeenCalledTimes(2);
      });
    });

    it("should handle errors", async () => {
      vi.mocked(magicUrlService.deactivateMagicLinks).mockRejectedValue(new Error("Remove failed"));

      const { result } = renderHook(() => useRemoveMagicUrl(), { wrapper: createWrapper() });

      result.current.mutate({ linkIds: ["link-1"], projectKey: TEST_PROJECT_KEY });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
