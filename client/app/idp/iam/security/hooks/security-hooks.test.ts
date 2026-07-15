import { createWrapper } from "@/test-utils/test-providers/query-client";
import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockToastFactory } from "@/test-utils/__mocks__";
import { securityService } from "../services/security.service";
import {
  useSecuritySummary,
  useUserSessions,
  useSessionDetails,
  useRevokeSession,
  useRevokeRefreshToken,
  useActivities,
  usePats,
  useGeneratePats,
} from "./index";

vi.mock("../services/security.service", () => ({
  securityService: {
    getSummary: vi.fn(),
    getSessions: vi.fn(),
    getSessionDetails: vi.fn(),
    revokeSession: vi.fn(),
    revokeRefreshToken: vi.fn(),
    getActivities: vi.fn(),
    getPats: vi.fn(),
    generatePats: vi.fn(),
  },
}));

vi.mock("@/hooks/use-toast", () => mockToastFactory());

// ─── Inline mock data ───────────────────────────────────────────────────────
const mockSummary = {
  currentSessionId: "session-1",
  totalSessions: 2,
  activeSessions: 1,
  expiredSessions: 1,
  revokedSessions: 0,
  lastActivityAt: "2025-01-01T00:00:00Z",
  lastLoginAt: "2025-01-01T00:00:00Z",
};

const mockSessions = [
  {
    sessionId: "session-1",
    tenantId: "tenant-1",
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

const makePat = (itemId: string, createdDate: string) => ({
  note: "token",
  itemId,
  createdDate,
  expiryDate: "2025-02-01T00:00:00Z",
  createdBy: "user-1",
  language: "en",
  lastUpdatedBy: "user-1",
  organizationIds: [],
  tags: [],
  code: "code",
  userId: "user-1",
  clientId: "client-1",
});

describe("Security Hooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ─── useSecuritySummary ────────────────────────────────────────────────────
  describe("useSecuritySummary", () => {
    it("should fetch the summary and forward the userId", async () => {
      vi.mocked(securityService.getSummary).mockResolvedValue(mockSummary);

      const { result } = renderHook(() => useSecuritySummary("user-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockSummary);
      expect(securityService.getSummary).toHaveBeenCalledWith("user-1");
    });

    it("should not fetch when disabled", async () => {
      vi.mocked(securityService.getSummary).mockResolvedValue(mockSummary);

      const { result } = renderHook(() => useSecuritySummary("user-1", { enabled: false }), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(securityService.getSummary).not.toHaveBeenCalled();
    });

    it("should surface errors", async () => {
      vi.mocked(securityService.getSummary).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useSecuritySummary(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useUserSessions ───────────────────────────────────────────────────────
  describe("useUserSessions", () => {
    it("should fetch sessions and forward the userId", async () => {
      vi.mocked(securityService.getSessions).mockResolvedValue(mockSessions);

      const { result } = renderHook(() => useUserSessions("user-1"), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockSessions);
      expect(securityService.getSessions).toHaveBeenCalledWith("user-1");
    });

    it("should not fetch when disabled", async () => {
      vi.mocked(securityService.getSessions).mockResolvedValue(mockSessions);

      const { result } = renderHook(() => useUserSessions("user-1", { enabled: false }), {
        wrapper: createWrapper(),
      });

      expect(result.current.fetchStatus).toBe("idle");
      expect(securityService.getSessions).not.toHaveBeenCalled();
    });
  });

  // ─── useSessionDetails ─────────────────────────────────────────────────────
  describe("useSessionDetails", () => {
    it("should fetch details for a sessionId", async () => {
      vi.mocked(securityService.getSessionDetails).mockResolvedValue(mockSessionDetails);

      const { result } = renderHook(() => useSessionDetails("session-1", "user-1"), {
        wrapper: createWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockSessionDetails);
      expect(securityService.getSessionDetails).toHaveBeenCalledWith("session-1", "user-1");
    });

    it("should be gated off when the sessionId is empty", async () => {
      vi.mocked(securityService.getSessionDetails).mockResolvedValue(mockSessionDetails);

      const { result } = renderHook(() => useSessionDetails(""), { wrapper: createWrapper() });

      expect(result.current.fetchStatus).toBe("idle");
      expect(securityService.getSessionDetails).not.toHaveBeenCalled();
    });
  });

  // ─── useRevokeSession ──────────────────────────────────────────────────────
  describe("useRevokeSession", () => {
    it("should revoke a session forwarding reason and userId", async () => {
      vi.mocked(securityService.revokeSession).mockResolvedValue(mockRevokeResponse);

      const { result } = renderHook(() => useRevokeSession("user-1"), { wrapper: createWrapper() });

      result.current.mutate({ sessionId: "session-1", reason: "manual" });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(securityService.revokeSession).toHaveBeenCalledWith("session-1", "manual", "user-1");
    });

    it("should surface errors", async () => {
      vi.mocked(securityService.revokeSession).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useRevokeSession(), { wrapper: createWrapper() });

      result.current.mutate({ sessionId: "session-1" });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useRevokeRefreshToken ─────────────────────────────────────────────────
  describe("useRevokeRefreshToken", () => {
    it("should revoke a refresh token forwarding reason and userId", async () => {
      vi.mocked(securityService.revokeRefreshToken).mockResolvedValue(mockRevokeResponse);

      const { result } = renderHook(() => useRevokeRefreshToken("user-1"), {
        wrapper: createWrapper(),
      });

      result.current.mutate({ tokenId: "token-1", reason: "leaked" });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(securityService.revokeRefreshToken).toHaveBeenCalledWith(
        "token-1",
        "leaked",
        "user-1",
      );
    });

    it("should surface errors", async () => {
      vi.mocked(securityService.revokeRefreshToken).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useRevokeRefreshToken(), { wrapper: createWrapper() });

      result.current.mutate({ tokenId: "token-1" });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });

  // ─── useActivities ─────────────────────────────────────────────────────────
  describe("useActivities", () => {
    it("should fetch activities forwarding the payload", async () => {
      vi.mocked(securityService.getActivities).mockResolvedValue(mockActivitiesResponse);

      const payload = { userId: "user-1", page: 0, pageSize: 10 };
      const { result } = renderHook(() => useActivities(payload), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockActivitiesResponse);
      expect(securityService.getActivities).toHaveBeenCalledWith(payload);
    });

    it("should not fetch when disabled", async () => {
      vi.mocked(securityService.getActivities).mockResolvedValue(mockActivitiesResponse);

      const { result } = renderHook(
        () => useActivities({ pageSize: 10 }, { enabled: false }),
        { wrapper: createWrapper() },
      );

      expect(result.current.fetchStatus).toBe("idle");
      expect(securityService.getActivities).not.toHaveBeenCalled();
    });
  });

  // ─── usePats ───────────────────────────────────────────────────────────────
  describe("usePats", () => {
    it("should fetch and sort PATs by createdDate descending", async () => {
      const older = makePat("pat-old", "2025-01-01T00:00:00Z");
      const newer = makePat("pat-new", "2025-06-01T00:00:00Z");
      vi.mocked(securityService.getPats).mockResolvedValue([older, newer]);

      const { result } = renderHook(() => usePats(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data?.map((p) => p.itemId)).toEqual(["pat-new", "pat-old"]);
    });

    it("should return an empty array when the response is not an array", async () => {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      vi.mocked(securityService.getPats).mockResolvedValue(null as any);

      const { result } = renderHook(() => usePats(), { wrapper: createWrapper() });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual([]);
    });
  });

  // ─── useGeneratePats ───────────────────────────────────────────────────────
  describe("useGeneratePats", () => {
    it("should generate a PAT successfully", async () => {
      vi.mocked(securityService.generatePats).mockResolvedValue([makePat("pat-1", "2025-01-01T00:00:00Z")]);

      const payload = { note: "ci token", codeTtlInMinute: 60, clientId: "client-1" };
      const { result } = renderHook(() => useGeneratePats(), { wrapper: createWrapper() });

      result.current.mutate(payload);

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(securityService.generatePats).toHaveBeenCalledWith(payload);
    });

    it("should surface errors", async () => {
      vi.mocked(securityService.generatePats).mockRejectedValue(new Error("boom"));

      const { result } = renderHook(() => useGeneratePats(), { wrapper: createWrapper() });

      result.current.mutate({ codeTtlInMinute: 60, clientId: "client-1" });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
