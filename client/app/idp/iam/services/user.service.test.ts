import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { serviceInstances } from "@/lib/http-client";
import { UserService } from "./user.service";
import { UserAccountService } from "./account.service";
import { USER_ENDPOINTS, ORGANIZATION_ENDPOINTS } from "../constants/endpoint.constant";
import { TEST_PROJECT_KEY } from "@/test-utils/__mocks__/data.mock";
import {
  mockGetUsersPayload,
  mockUsersResponse,
  mockUser,
  mockCreateUserPayload,
  mockUpdateUserPayload,
  mockSignUpSettingResponse,
  mockSaveSignUpSettingPayload,
  mockSaveRolesAndPermissionsPayload,
  mockGetHistoriesPayload,
  mockGeneratePATPayload,
  mockGetUserRolesPayload,
  mockGetUserPermissionsPayload,
  mockResendActivationPayload,
  mockSuccessResponse,
  MOCK_USER_ITEM_ID,
} from "../../test-utils/__mocks__";

vi.mock("@/lib/http-client", () => mockHttpClientFactory());
vi.mock("@/lib/utils", () => ({
  parseMongoDBString: vi.fn((str: string) => str),
}));

describe("UserService", () => {
  let service: UserService;

  beforeEach(() => {
    service = new UserService(new UserAccountService());
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  // ─── getUsers ─────────────────────────────────────────────────────────────
  describe("getUsers", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockUsersResponse);

      const result = await service.getUsers(mockGetUsersPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USERS, mockGetUsersPayload);
      expect(result).toEqual(mockUsersResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getUsers(mockGetUsersPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUser ──────────────────────────────────────────────────────────────
  describe("getUser", () => {
    it("should GET from the correct endpoint", async () => {
      const mockResponse = { data: mockUser };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUser();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER);
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUser()).rejects.toThrow("Network error");
    });
  });

  // ─── getUserById ──────────────────────────────────────────────────────────
  describe("getUserById", () => {
    it("should GET with correct query params", async () => {
      const payload = { id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY };
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });

      const result = await service.getUserById(payload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER}?id=${payload.id}&ProjectKey=${payload.projectKey}`,
      );
      expect(result).toEqual({ data: mockUser });
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getUserById({ id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── addUser ──────────────────────────────────────────────────────────────
  describe("addUser", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.addUser(mockCreateUserPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.CREATE, mockCreateUserPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.addUser(mockCreateUserPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── updateUser ───────────────────────────────────────────────────────────
  describe("updateUser", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateUser(mockUpdateUserPayload);

      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.UPDATE, mockUpdateUserPayload);
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updateUser(mockUpdateUserPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getSignUpSetting ─────────────────────────────────────────────────────
  describe("getSignUpSetting", () => {
    it("should GET the signup-settings endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSignUpSettingResponse);

      const result = await service.getSignUpSetting();

      expect(http.get).toHaveBeenCalledWith(ORGANIZATION_ENDPOINTS.GET_SIGNUP_SETTING);
      expect(result).toEqual(mockSignUpSettingResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSignUpSetting()).rejects.toThrow("Network error");
    });
  });

  // ─── saveSignUpSetting ────────────────────────────────────────────────────
  describe("saveSignUpSetting", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveSignUpSetting(mockSaveSignUpSettingPayload);

      expect(http.post).toHaveBeenCalledWith(
        ORGANIZATION_ENDPOINTS.SAVE_SIGNUP_SETTING,
        mockSaveSignUpSettingPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.saveSignUpSetting(mockSaveSignUpSettingPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── saveRolesAndPermissions ──────────────────────────────────────────────
  describe("saveRolesAndPermissions", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.saveRolesAndPermissions(mockSaveRolesAndPermissionsPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS,
        mockSaveRolesAndPermissionsPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(
        service.saveRolesAndPermissions(mockSaveRolesAndPermissionsPayload),
      ).rejects.toThrow("Network error");
    });
  });

  // ─── getSecurityOverview ───────────────────────────────────────────────────
  describe("getSecurityOverview", () => {
    it("should GET the overview endpoint", async () => {
      const rawResponse = {
        currentSessionId: "s-current",
        sessionGroups: [
          {
            sessionId: "s1",
            userId: "u1",
            tenantId: "t1",
            lastActivityAt: "2026-07-09T08:54:45.816Z",
            isCurrent: true,
            apps: [
              {
                tokenId: "t1",
                sessionId: "s1",
                userId: "u1",
                tenantId: "t1",
                clientId: "c1",
                deviceName: "Chrome",
                operatingSystem: "macOS",
                browser: "Chrome",
                ipAddresses: "127.0.0.1",
                issuedUtc: "2026-07-09T08:54:45.816Z",
                absoluteExpiry: "2026-09-07T08:54:45.816Z",
                isActive: true,
                impersonated: false,
              },
            ],
          },
        ],
        idpSession: null,
        activeImpersonations: [],
      };
      vi.mocked(http.get).mockResolvedValue(rawResponse);

      const result = await service.getSecurityOverview();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_SECURITY_OVERVIEW);
      expect(result.currentSessionId).toBe("s-current");
      expect(result.sessionGroups).toHaveLength(1);
      expect(result.sessionGroups[0].apps).toHaveLength(1);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSecurityOverview()).rejects.toThrow("Network error");
    });
  });

  // ─── getSessionTimeline ──────────────────────────────────────────────────
  describe("getSessionTimeline", () => {
    it("should GET the session timeline endpoint", async () => {
      const rawResponse = {
        sessionId: "s1",
        session: { sessionId: "s1" },
        revokedAccessTokens: [],
        lifecycle: [],
        rotations: [],
      };
      vi.mocked(http.get).mockResolvedValue(rawResponse);

      const result = await service.getSessionTimeline("s1");

      expect(http.get).toHaveBeenCalledWith(`${USER_ENDPOINTS.REVOKE_SESSION}/s1`);
      expect(result.sessionId).toBe("s1");
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSessionTimeline("s1")).rejects.toThrow("Network error");
    });
  });

  // ─── revokeRefreshToken ──────────────────────────────────────────────────
  describe("revokeRefreshToken", () => {
    it("should POST to the revoke-refresh-token endpoint with reason", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.revokeRefreshToken("tok-1", "user_revoked");

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.REVOKE_REFRESH_TOKEN.replace("{tokenId}", "tok-1"),
        { reason: "user_revoked" },
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should POST without a body when no reason is supplied", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      await service.revokeRefreshToken("tok-2");

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.REVOKE_REFRESH_TOKEN.replace("{tokenId}", "tok-2"),
        {},
      );
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.revokeRefreshToken("tok-3")).rejects.toThrow("Network error");
    });
  });

  // ─── getActivities ───────────────────────────────────────────────────────
  describe("getActivities", () => {
    it("should POST to the activities endpoint with the correct body and return response as-is", async () => {
      const rawResponse = {
        data: [
          {
            itemId: "a1",
            userId: "u1",
            actorUserId: "u1",
            category: "Auth",
            event: "LOGIN_SUCCESS",
            context: { ipAddress: "127.0.0.1" },
            createdDate: "2026-07-09T08:56:18.707Z",
          },
        ],
        totalCount: 1,
        errors: null,
      };
      vi.mocked(http.post).mockResolvedValue(rawResponse);

      const result = await service.getActivities(mockGetHistoriesPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.GET_ACTIVITIES.replace(
          "{userId}",
          encodeURIComponent(mockGetHistoriesPayload.userId),
        ),
        {
          page: mockGetHistoriesPayload.page,
          pageSize: mockGetHistoriesPayload.pageSize,
        },
      );
      expect(result.totalCount).toBe(1);
      expect(result.data[0].event).toBe("LOGIN_SUCCESS");
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.getActivities(mockGetHistoriesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getPats ──────────────────────────────────────────────────────────────
  describe("getPats", () => {
    it("should GET from the correct endpoint", async () => {
      const mockResponse = { data: [], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getPats();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER_CODES);
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getPats()).rejects.toThrow("Network error");
    });
  });

  // ─── generatePats ─────────────────────────────────────────────────────────
  describe("generatePats", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.generatePats(mockGeneratePATPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.GENERATE_USER_CODE,
        mockGeneratePATPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.generatePats(mockGeneratePATPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUserRoles ─────────────────────────────────────────────────────────
  describe("getUserRoles", () => {
    it("should GET with correct query params", async () => {
      const mockResponse = { data: ["admin"], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUserRoles(mockGetUserRolesPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${mockGetUserRolesPayload.userId}&ProjectKey=${mockGetUserRolesPayload.projectKey}`,
      );
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUserRoles(mockGetUserRolesPayload)).rejects.toThrow("Network error");
    });
  });

  // ─── getUserPermissions ───────────────────────────────────────────────────
  describe("getUserPermissions", () => {
    it("should GET with correct query params", async () => {
      const mockResponse = { data: ["read"], errors: null };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUserPermissions(mockGetUserPermissionsPayload);

      expect(http.get).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${mockGetUserPermissionsPayload.userId}&ProjectKey=${mockGetUserPermissionsPayload.projectKey}`,
      );
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUserPermissions(mockGetUserPermissionsPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });

  // ─── accountDeactivate ────────────────────────────────────────────────────
  describe("accountDeactivate", () => {
    it("should POST to the correct endpoint with payload", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.accountDeactivate(mockResendActivationPayload);

      expect(http.post).toHaveBeenCalledWith(
        USER_ENDPOINTS.DEACTIVATE,
        mockResendActivationPayload,
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.accountDeactivate(mockResendActivationPayload)).rejects.toThrow(
        "Network error",
      );
    });
  });
});
