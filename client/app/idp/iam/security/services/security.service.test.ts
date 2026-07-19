import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { SecurityService } from "./security.service";
import { SECURITY_ENDPOINTS } from "../constants/security-endpoints";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());

// ─── Inline mock data ───────────────────────────────────────────────────────
const mockSummary = {
  currentSessionId: "session-1",
  totalSessions: 3,
  activeSessions: 2,
  expiredSessions: 1,
  revokedSessions: 0,
  lastActivityAt: "2025-01-01T00:00:00Z",
  lastLoginAt: "2025-01-01T00:00:00Z",
};

const mockSessions = [
  {
    sessionId: "session-1",
    tenantId: "tenant-1",
    userId: "user-1",
    createdAt: "2025-01-01T00:00:00Z",
    lastActivityAt: "2025-01-01T00:00:00Z",
    absoluteExpiry: "2025-01-02T00:00:00Z",
    idleExpiry: "2025-01-01T01:00:00Z",
    isCurrent: true,
    status: "Active",
    applicationCount: 1,
    clientIds: ["client-1"],
  },
];

const mockSessionDetails = { overview: null, applications: [], timeline: [] };

const mockRevokeResponse = {
  sessionId: "session-1",
  alreadyRevoked: false,
  revokedAt: "2025-01-01T00:00:00Z",
  reason: "manual",
  revokedRefreshTokens: 1,
  clientId: "client-1",
  warnings: [],
};

const mockActivitiesResponse = { items: [], totalCount: 0, page: 0, pageSize: 10 };

const mockPats = [
  {
    note: "token",
    itemId: "pat-1",
    createdDate: "2025-01-01T00:00:00Z",
    expiryDate: "2025-02-01T00:00:00Z",
    createdBy: "user-1",
    language: "en",
    lastUpdatedBy: "user-1",
    organizationIds: [],
    tags: [],
    code: "abc123",
    userId: "user-1",
    clientId: "client-1",
  },
];

const mockGeneratePatPayload = { note: "new token", codeTtlInMinute: 60, clientId: "client-1" };

describe("SecurityService", () => {
  let service: SecurityService;

  beforeEach(() => {
    service = new SecurityService();
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getSummary ────────────────────────────────────────────────────────────
  describe("getSummary", () => {
    it("should GET the summary endpoint without a uid", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSummary);

      const result = await service.getSummary();

      expect(http.get).toHaveBeenCalledWith(SECURITY_ENDPOINTS.SUMMARY);
      expect(result).toEqual(mockSummary);
    });

    it("should append the uid query param when provided", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSummary);

      await service.getSummary("user 1");

      expect(http.get).toHaveBeenCalledWith(`${SECURITY_ENDPOINTS.SUMMARY}?uid=user%201`);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSummary()).rejects.toThrow("Network error");
    });
  });

  // ─── getSessions ───────────────────────────────────────────────────────────
  describe("getSessions", () => {
    it("should GET the sessions endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSessions);

      const result = await service.getSessions();

      expect(http.get).toHaveBeenCalledWith(SECURITY_ENDPOINTS.SESSIONS);
      expect(result).toEqual(mockSessions);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSessions()).rejects.toThrow("Network error");
    });
  });

  // ─── getSessionDetails ─────────────────────────────────────────────────────
  describe("getSessionDetails", () => {
    it("should GET the session-details endpoint with the sessionId injected", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSessionDetails);

      const result = await service.getSessionDetails("session-1");

      expect(http.get).toHaveBeenCalledWith(
        SECURITY_ENDPOINTS.SESSION_DETAILS.replace("{sessionId}", "session-1"),
      );
      expect(result).toEqual(mockSessionDetails);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSessionDetails("session-1")).rejects.toThrow("Network error");
    });
  });

  // ─── revokeSession ─────────────────────────────────────────────────────────
  describe("revokeSession", () => {
    it("should POST reason and userId in the body", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRevokeResponse);

      const result = await service.revokeSession("session-1", "manual", "user-1");

      expect(http.post).toHaveBeenCalledWith(
        SECURITY_ENDPOINTS.REVOKE_SESSION.replace("{sessionId}", "session-1"),
        { reason: "manual", userId: "user-1" },
      );
      expect(result).toEqual(mockRevokeResponse);
    });

    it("should POST an empty body when reason and userId are omitted", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRevokeResponse);

      await service.revokeSession("session-1");

      expect(http.post).toHaveBeenCalledWith(
        SECURITY_ENDPOINTS.REVOKE_SESSION.replace("{sessionId}", "session-1"),
        {},
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.revokeSession("session-1")).rejects.toThrow("Network error");
    });
  });

  // ─── revokeRefreshToken ────────────────────────────────────────────────────
  describe("revokeRefreshToken", () => {
    it("should POST reason and userId in the body", async () => {
      vi.mocked(http.post).mockResolvedValue(mockRevokeResponse);

      const result = await service.revokeRefreshToken("token-1", "leaked", "user-1");

      expect(http.post).toHaveBeenCalledWith(
        SECURITY_ENDPOINTS.REVOKE_REFRESH_TOKEN.replace("{tokenId}", "token-1"),
        { reason: "leaked", userId: "user-1" },
      );
      expect(result).toEqual(mockRevokeResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.revokeRefreshToken("token-1")).rejects.toThrow("Network error");
    });
  });

  // ─── getActivities ─────────────────────────────────────────────────────────
  describe("getActivities", () => {
    it("should POST only the provided fields", async () => {
      vi.mocked(http.post).mockResolvedValue(mockActivitiesResponse);

      const result = await service.getActivities({
        userId: "user-1",
        page: 0,
        pageSize: 10,
        filter: { search: "login" },
      });

      expect(http.post).toHaveBeenCalledWith(SECURITY_ENDPOINTS.ACTIVITY, {
        userId: "user-1",
        page: 0,
        pageSize: 10,
        filter: { search: "login" },
      });
      expect(result).toEqual(mockActivitiesResponse);
    });

    it("should POST an empty body when payload is empty", async () => {
      vi.mocked(http.post).mockResolvedValue(mockActivitiesResponse);

      await service.getActivities({});

      expect(http.post).toHaveBeenCalledWith(SECURITY_ENDPOINTS.ACTIVITY, {});
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getActivities({})).rejects.toThrow("Network error");
    });
  });

  // ─── getPats ───────────────────────────────────────────────────────────────
  describe("getPats", () => {
    it("should GET the user-codes endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockPats);

      const result = await service.getPats();

      expect(http.get).toHaveBeenCalledWith(SECURITY_ENDPOINTS.GET_USER_CODES);
      expect(result).toEqual(mockPats);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getPats()).rejects.toThrow("Network error");
    });
  });

  // ─── generatePats ──────────────────────────────────────────────────────────
  describe("generatePats", () => {
    it("should POST the payload to the generate endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockPats);

      const result = await service.generatePats(mockGeneratePatPayload);

      expect(http.post).toHaveBeenCalledWith(
        SECURITY_ENDPOINTS.GENERATE_USER_CODE,
        mockGeneratePatPayload,
      );
      expect(result).toEqual(mockPats);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.generatePats(mockGeneratePatPayload)).rejects.toThrow("Network error");
    });
  });
});
