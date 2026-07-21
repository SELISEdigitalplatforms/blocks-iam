import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { MagicUrlService } from "./magic-url.service";
import { MAGIC_URL_ENDPOINTS } from "@blocks-utilities/constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

const http = serviceInstances.idpService;

// ─── Inline mock data ────────────────────────────────────────────────────────
const mockMagicUrl = {
  itemId: "link-1",
  uri: "https://example.com/action",
  usageLimit: 10,
  usageCount: 0,
  shortUri: "https://dev-short.seliseblocks.com/abc",
  createdAt: "2025-01-01T00:00:00Z",
  status: "Active",
};

const mockGetLinkResponse = { data: mockMagicUrl, errors: null };

const mockGetLinksResponse = {
  data: [mockMagicUrl],
  errors: [{ field: "x", message: "y" }],
  totalCount: 42,
};

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

describe("MagicUrlService", () => {
  let service: MagicUrlService;

  beforeEach(() => {
    service = new MagicUrlService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getMagicUrl ───────────────────────────────────────────────────────────
  describe("getMagicUrl", () => {
    it("should GET the link endpoint and unwrap .data", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetLinkResponse);

      const result = await service.getMagicUrl({ ItemId: "link-1", projectKey: TEST_PROJECT_KEY });

      expect(http.get).toHaveBeenCalledWith(
        `${MAGIC_URL_ENDPOINTS.GET_LINK}?ItemId=link-1&ProjectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockMagicUrl);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getMagicUrl({ ItemId: "link-1", projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getMagicUrls ──────────────────────────────────────────────────────────
  describe("getMagicUrls", () => {
    it("should GET with only the required params when no optional filters are supplied", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetLinksResponse);

      const result = await service.getMagicUrls({
        page: 1,
        pageSize: 10,
        projectKey: TEST_PROJECT_KEY,
      });

      const expectedParams = new URLSearchParams({
        PageSize: "10",
        PageNumber: "1",
        ProjectKey: TEST_PROJECT_KEY,
      });
      expect(http.get).toHaveBeenCalledWith(
        `${MAGIC_URL_ENDPOINTS.GET_LINKS}?${expectedParams.toString()}`,
      );
      expect(result).toEqual({
        data: [mockMagicUrl],
        errors: mockGetLinksResponse.errors,
        totalCount: 42,
      });
    });

    it("should append every optional filter to the query params", async () => {
      vi.mocked(http.get).mockResolvedValue(mockGetLinksResponse);

      await service.getMagicUrls({
        page: 2,
        pageSize: 20,
        projectKey: TEST_PROJECT_KEY,
        searchText: "hello",
        status: "Active",
        requestMethod: "GET",
        type: "1",
        expiryDateRangeStartDate: "2024-01-01",
        expiryDateRangeEndDate: "2024-01-31",
      });

      const expectedParams = new URLSearchParams({
        PageSize: "20",
        PageNumber: "2",
        ProjectKey: TEST_PROJECT_KEY,
      });
      expectedParams.append("SearchText", "hello");
      expectedParams.append("Status", "Active");
      expectedParams.append("RequestMethod", "GET");
      expectedParams.append("Type", "1");
      expectedParams.append("ExpiryDateRange.StartDate", "2024-01-01");
      expectedParams.append("ExpiryDateRange.EndDate", "2024-01-31");

      expect(http.get).toHaveBeenCalledWith(
        `${MAGIC_URL_ENDPOINTS.GET_LINKS}?${expectedParams.toString()}`,
      );
    });

    it("should fall back to [] errors and 0 totalCount when absent", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [mockMagicUrl] });

      const result = await service.getMagicUrls({
        page: 1,
        pageSize: 10,
        projectKey: TEST_PROJECT_KEY,
      });

      expect(result).toEqual({ data: [mockMagicUrl], errors: [], totalCount: 0 });
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getMagicUrls({ page: 1, pageSize: 10, projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── createMagicUrl ────────────────────────────────────────────────────────
  describe("createMagicUrl", () => {
    it("should POST to the create endpoint with payload and return the response", async () => {
      vi.mocked(http.post).mockResolvedValue(mockMagicUrl);

      const result = await service.createMagicUrl(mockCreatePayload);

      expect(http.post).toHaveBeenCalledWith(MAGIC_URL_ENDPOINTS.CREATE_LINK, mockCreatePayload);
      expect(result).toEqual(mockMagicUrl);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.createMagicUrl(mockCreatePayload)).rejects.toThrow("Network error");
    });
  });

  // ─── saveMagicUrlConfig ────────────────────────────────────────────────────
  describe("saveMagicUrlConfig", () => {
    it("should POST to the save-config endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockConfigResponse);

      const result = await service.saveMagicUrlConfig(mockSaveConfigPayload);

      expect(http.post).toHaveBeenCalledWith(MAGIC_URL_ENDPOINTS.SAVE_CONFIG, mockSaveConfigPayload);
      expect(result).toEqual(mockConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveMagicUrlConfig(mockSaveConfigPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── getMagicUrlConfig ─────────────────────────────────────────────────────
  describe("getMagicUrlConfig", () => {
    it("should GET the config endpoint with the project key", async () => {
      vi.mocked(http.get).mockResolvedValue(mockConfigResponse);

      const result = await service.getMagicUrlConfig(TEST_PROJECT_KEY);

      expect(http.get).toHaveBeenCalledWith(
        `${MAGIC_URL_ENDPOINTS.GET_CONFIG}?ProjectKey=${TEST_PROJECT_KEY}`,
      );
      expect(result).toEqual(mockConfigResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getMagicUrlConfig(TEST_PROJECT_KEY)).rejects.toThrow("Network error");
    });
  });

  // ─── deactivateMagicLinks ──────────────────────────────────────────────────
  describe("deactivateMagicLinks", () => {
    it("should POST to the remove-links endpoint with the payload", async () => {
      vi.mocked(http.post).mockResolvedValue(undefined);

      const payload = { linkIds: ["link-1", "link-2"], projectKey: TEST_PROJECT_KEY };
      await service.deactivateMagicLinks(payload);

      expect(http.post).toHaveBeenCalledWith(MAGIC_URL_ENDPOINTS.REMOVE_LINKS, payload);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.deactivateMagicLinks({ linkIds: ["link-1"], projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });
});
