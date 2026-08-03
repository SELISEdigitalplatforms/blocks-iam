import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { mockHttpClientFactory } from "@/test-utils/__mocks__";
import { http } from "@/lib/http-client";
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

  describe("getUser", () => {
    it("should GET from the correct endpoint", async () => {
      const mockResponse = { data: mockUser };
      vi.mocked(http.get).mockResolvedValue(mockResponse);

      const result = await service.getUser();

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER, undefined, {
        absoluteUrl: true,
      });
      expect(result).toEqual(mockResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getUser()).rejects.toThrow("Network error");
    });
  });

  describe("getUserById", () => {
    it("should GET with correct query params", async () => {
      const payload = { id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY };
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });

      const result = await service.getUserById(payload);

      expect(http.get).toHaveBeenCalledWith(`${USER_ENDPOINTS.GET_USER}/${payload.id}`);
      // Response goes through normalizeUserFromApi; core identity fields are preserved.
      expect(result.data.itemId).toBe(MOCK_USER_ITEM_ID);
      expect(result.data.email).toBe(mockUser.email);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(
        service.getUserById({ id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY }),
      ).rejects.toThrow("Network error");
    });

    it("should normalize flat roles and permissions into per-organization maps", async () => {
      const payload = { id: MOCK_USER_ITEM_ID, projectKey: TEST_PROJECT_KEY };
      vi.mocked(http.get).mockResolvedValue({
        data: {
          itemId: MOCK_USER_ITEM_ID,
          organizationIds: ["default"],
          roles: ["test", "user"],
          permissions: ["Change User Password", "View Client Credentials"],
        },
      });

      const result = await service.getUserById(payload);

      expect(result.data.roles).toEqual({ default: ["test", "user"] });
      expect(result.data.permissions).toEqual({
        default: ["Change User Password", "View Client Credentials"],
      });
      expect(result.data.OrganizationsRoles).toEqual({ default: ["test", "user"] });
      expect(result.data.OrganizationsPermissions).toEqual({
        default: ["Change User Password", "View Client Credentials"],
      });
    });
  });

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

  describe("updateUser", () => {
    it("should fetch the current user and POST the merged payload", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);

      const result = await service.updateUser(mockUpdateUserPayload);

      const postedBody = vi.mocked(http.post).mock.calls[0][1] as Record<string, unknown>;
      // payload only overrides firstName; every other field is merged from the
      // freshly-fetched current record so the server doesn't wipe them.
      expect(postedBody).toMatchObject({
        itemId: mockUpdateUserPayload.itemId,
        firstName: mockUpdateUserPayload.firstName,
        lastName: mockUser.lastName,
        email: mockUser.email,
        active: mockUser.active,
        mfaEnabled: mockUser.mfaEnabled,
      });
      // No raw payload — current user fields must be merged in
      expect(postedBody).not.toEqual(mockUpdateUserPayload);
      expect(http.post).toHaveBeenCalledWith(
        `${USER_ENDPOINTS.UPDATE}/${mockUpdateUserPayload.itemId}`,
        expect.objectContaining({ itemId: mockUpdateUserPayload.itemId }),
      );
      expect(result).toEqual(mockSuccessResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });
      vi.mocked(http.post).mockRejectedValue(new Error("Network error"));

      await expect(service.updateUser(mockUpdateUserPayload)).rejects.toThrow("Network error");
    });
  });

  describe("getSignUpSetting", () => {
    it("should GET the signup-settings endpoint", async () => {
      vi.mocked(http.get).mockResolvedValue(mockSignUpSettingResponse);

      const result = await service.getSignUpSetting();

      expect(http.get).toHaveBeenCalledWith(
        ORGANIZATION_ENDPOINTS.GET_SIGNUP_SETTING,
        {},
        undefined,
      );
      expect(result).toEqual(mockSignUpSettingResponse);
    });

    it("should throw when the API call fails", async () => {
      vi.mocked(http.get).mockRejectedValue(new Error("Network error"));

      await expect(service.getSignUpSetting()).rejects.toThrow("Network error");
    });
  });

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

  describe("simple read endpoints", () => {
    it("me() reads the current user with an absolute url", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: mockUser });
      const result = await service.me();
      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.ME, undefined, { absoluteUrl: true });
      expect(result).toEqual({ data: mockUser });
    });

    it("getUserInfo() reads the auth user info", async () => {
      vi.mocked(http.get).mockResolvedValue(mockUser);
      await service.getUserInfo();
      expect(http.get).toHaveBeenCalled();
    });

    it("isUserExist() encodes the email in the query", async () => {
      vi.mocked(http.get).mockResolvedValue({ userId: "u1" });
      await service.isUserExist("a+b@test.com");
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("a%2Bb%40test.com"));
    });

    it("getUserRoles() requests the roles for a user id", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] });
      await service.getUserRoles({ userId: "u1", projectKey: "p1" });
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("Id=u1"));
    });

    it("getUserPermissions() requests the permissions for a user id", async () => {
      vi.mocked(http.get).mockResolvedValue({ data: [] });
      await service.getUserPermissions({ userId: "u1", projectKey: "p1" });
      expect(http.get).toHaveBeenCalledWith(expect.stringContaining("Id=u1"));
    });
  });

  describe("write endpoints", () => {
    it("updateUserAccessControl() posts to the access-control endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);
      await service.updateUserAccessControl({ itemId: "u1" } as never);
      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.ACCESS_CONTROL, { itemId: "u1" });
    });

    it("revokeAccess() posts to the revoke-access endpoint", async () => {
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);
      await service.revokeAccess({ userId: "u1", organizationId: "o1" } as never);
      expect(http.post).toHaveBeenCalledWith(USER_ENDPOINTS.REVOKE_ACCESS, {
        userId: "u1",
        organizationId: "o1",
      });
    });
  });

  describe("getUserById normalization", () => {
    it("scopes array roles and permissions across the user's organizations", async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: {
          itemId: "u1",
          organizationIds: ["o1", "o2"],
          roles: ["admin"],
          permissions: ["read"],
        },
      });
      const result = await service.getUserById({ id: "u1", projectKey: "" });
      expect(result.data.roles).toEqual({ o1: ["admin"], o2: ["admin"] });
      expect(result.data.permissions).toEqual({ o1: ["read"], o2: ["read"] });
    });

    it("falls back to the PascalCase OrganizationIds field", async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { itemId: "u1", OrganizationIds: ["o9"], roles: ["viewer"] },
      });
      const result = await service.getUserById({ id: "u1", projectKey: "" });
      expect(result.data.organizationIds).toEqual(["o9"]);
      expect(result.data.roles).toEqual({ o9: ["viewer"] });
    });
  });

  describe("updateMe", () => {
    it("merges the current record with the payload and posts to update-me", async () => {
      vi.mocked(http.get).mockResolvedValue({
        data: { itemId: "u1", firstName: "Ada", organizationIds: ["o1"], roles: { o1: ["admin"] } },
      });
      vi.mocked(http.post).mockResolvedValue(mockSuccessResponse);
      await service.updateMe({ itemId: "u1", lastName: "Lovelace" } as never);
      const [endpoint, body] = vi.mocked(http.post).mock.calls[0];
      expect(endpoint).toBe(USER_ENDPOINTS.UPDATE_ME);
      // Untouched fields survive, requested changes and flattened roles applied.
      expect(body).toMatchObject({ firstName: "Ada", lastName: "Lovelace", roles: ["admin"] });
    });
  });
});