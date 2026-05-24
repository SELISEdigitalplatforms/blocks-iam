import { http } from "@/lib/http-client";
import { parseMongoDBString } from "@/lib/utils";
import {
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  ICreateUserPayload,
  ICreateUserResponse,
  IDeviceSessionResponse,
  IGeneratePATPayload,
  IGetHistoriesPayload,
  IGetSessionPayload,
  IGetUserByIdPayload,
  IGetUserByIdResponse,
  IGetUserPermissionsPayload,
  IGetUserPermissionsResponse,
  IGetUserRolesPayload,
  IGetUserRolesResponse,
  IGetUsersPayload,
  IGetUsersResponse,
  IHistoriesResponse,
  IPATResponse,
  ISaveRolesAndPermissionsPayload,
  ISaveRolesAndPermissionsResponse,
  IUpdateUserPayload,
  IUpdateUserResponse,
  IGetSignUpSettingPayload,
  IGetSignUpSettingResponse,
  ISaveSignUpSettingPayload,
  ISaveSignUpSettingResponse,
  User,
} from "@blocks-idp/iam/models/user";
import { UserAccountService } from "./account.service";
import {
  USER_ENDPOINTS,
  ORGANIZATION_ENDPOINTS,
} from "../constants/endpoint.constant";
import { AUTH_ENDPOINTS } from "@/idp/authentication/constants/endpoint.constant";

export class UserService {
  constructor(public account: UserAccountService) {}

  getUsers(
    payload: Omit<IGetUsersPayload, "projectKey">,
  ): Promise<IGetUsersResponse> {
    const params = new URLSearchParams();
    params.set("page", String(payload.page));
    params.set("pageSize", String(payload.pageSize));
    if (payload.sort) {
      params.set("sort.property", payload.sort.property);
      params.set("sort.isDescending", String(payload.sort.isDescending));
    }
    if (payload.filter) {
      if (payload.filter.email)
        params.set("filter.email", payload.filter.email);
      if (payload.filter.name) params.set("filter.name", payload.filter.name);
      if (payload.filter.organizationId)
        params.set("filter.organizationId", payload.filter.organizationId);
    }
    return http.get(`${USER_ENDPOINTS.GET_USERS}?${params.toString()}`);
  }

  getUser(): Promise<{ data: User }> {
    return http.get(`${USER_ENDPOINTS.GET_USER}`, undefined, {
      absoluteUrl: true,
    });
  }

  me(): Promise<{ data: User }> {
    return http.get(`${USER_ENDPOINTS.ME}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserInfo(): Promise<User> {
    return http.get(`${AUTH_ENDPOINTS.USER_INFO}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserById(payload: IGetUserByIdPayload): Promise<IGetUserByIdResponse> {
    return http.get(`${USER_ENDPOINTS.GET_USER}/${payload.id}`);
  }

  addUser(createPayload: ICreateUserPayload): Promise<ICreateUserResponse> {
    return http.post(USER_ENDPOINTS.CREATE, createPayload);
  }

  updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    const flattenRecord = (value: unknown): string[] => {
      if (!value) return [];
      if (Array.isArray(value)) return value as string[];
      return Object.values(value as Record<string, string[]>).flat();
    };
    const normalized = {
      lastLoggedInTime: payload.lastLoggedInTime,
      lastLoggedInDeviceInfo: payload.lastLoggedInDeviceInfo,
      logInCount: payload.logInCount,
      itemId: payload.itemId,
      createdDate: payload.createdDate,
      lastUpdatedDate: payload.lastUpdatedDate,
      language: payload.language,
      salutation: payload.salutation,
      firstName: payload.firstName,
      lastName: payload.lastName,
      email: payload.email,
      userName: payload.userName,
      phoneNumber: payload.phoneNumber,
      organizationIds: payload.organizationIds,
      lastUsedOrganizationId: payload.lastUsedOrganizationId,
      roles: flattenRecord(payload.roles),
      permissions: flattenRecord(payload.permissions),
      active: payload.active,
      status: payload.status,
      statusReason: payload.statusReason,
      deactivatedAtUtc: payload.deactivatedAtUtc,
      isVerified: payload.isVerified,
      emailVerifiedAtUtc: payload.emailVerifiedAtUtc,
      phoneVerifiedAtUtc: payload.phoneVerifiedAtUtc,
      profileImageUrl: payload.profileImageUrl,
      mfaEnabled: payload.mfaEnabled,
      isMfaVerified: payload.isMfaVerified,
      userMfaType: payload.userMfaType,
      provisioningSource: payload.provisioningSource,
      externalIdentities: payload.externalIdentities,
      userCreationType: payload.userCreationType,
      department: payload.department,
      employeeId: payload.employeeId,
      isMultiOrgEnabled: payload.isMultiOrgEnabled,
      organizations: payload.organizations,
      projectKey: payload.projectKey,
      profileImageId: payload.profileImageId,
    };
    return http.post(`/api/iam/users/${payload.itemId}`, normalized);
  }

  getSignUpSetting(): Promise<IGetSignUpSettingResponse> {
    return http.get(`${ORGANIZATION_ENDPOINTS.GET_SIGNUP_SETTING}`);
  }

  saveSignUpSetting(
    payload: ISaveSignUpSettingPayload,
  ): Promise<ISaveSignUpSettingResponse> {
    return http.post(ORGANIZATION_ENDPOINTS.SAVE_SIGNUP_SETTING, payload);
  }

  saveRolesAndPermissions(
    payload: ISaveRolesAndPermissionsPayload,
  ): Promise<ISaveRolesAndPermissionsResponse> {
    return http.post(USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS, payload);
  }

  async getSessions(
    payload: IGetSessionPayload,
  ): Promise<IDeviceSessionResponse> {
    const res = await http.get<{
      data: string[];
      errors: unknown;
      totalCount: number;
    }>(
      `${USER_ENDPOINTS.GET_SESSIONS}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getHistories(
    payload: IGetHistoriesPayload,
  ): Promise<IHistoriesResponse> {
    const res = await http.get<{
      data: string[];
      errors: unknown;
      totalCount: number;
    }>(
      `${USER_ENDPOINTS.GET_HISTORIES}?page=${payload.page}&pageSize=${payload.pageSize}&projectkey=${payload.projectKey}&filter.userId=${payload.filter.UserId}`,
    );
    return {
      data: res.data.map((item) => JSON.parse(parseMongoDBString(item))),
      totalCount: res.totalCount,
      errors: res.errors,
    };
  }

  async getPats(): Promise<IPATResponse> {
    return http.get(USER_ENDPOINTS.GET_USER_CODES);
  }

  async generatePats(payload: IGeneratePATPayload): Promise<IPATResponse> {
    return http.post(USER_ENDPOINTS.GENERATE_USER_CODE, payload);
  }

  getUserRoles(payload: IGetUserRolesPayload): Promise<IGetUserRolesResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${payload.userId}`,
    );
  }

  getUserPermissions(
    payload: IGetUserPermissionsPayload,
  ): Promise<IGetUserPermissionsResponse> {
    return http.get(
      `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${payload.userId}`,
    );
  }

  accountDeactivate(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return http.post(USER_ENDPOINTS.DEACTIVATE, payload);
  }
}

export const userService = new UserService(new UserAccountService());
