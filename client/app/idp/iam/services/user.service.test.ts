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

      expect(http.get).toHaveBeenCalledWith(USER_ENDPOINTS.GET_USER);
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
      expect(postedBody).toMatchObject({
        itemId: mockUpdateUserPayload.itemId,
        firstName: mockUpdateUserPayload.firstName,
        lastName: mockUpdateUserPayload.lastName,
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

      expect(http.get).toHaveBeenCalledWith(ORGANIZATION_ENDPOINTS.GET_SIGNUP_SETTING);
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
});