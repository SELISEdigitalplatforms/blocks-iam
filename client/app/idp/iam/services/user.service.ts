import { serviceInstances } from "@/lib/http-client";
import { parseMongoDBString } from "@/lib/utils";
import {
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  ICreateUserPayload,
  ICreateUserResponse,
  IRevokeSessionResponse,
  IGeneratePATPayload,
  IGetUserByIdPayload,
  IGetUserByIdResponse,
  IGetUserPermissionsPayload,
  IGetUserPermissionsResponse,
  IGetUserRolesPayload,
  IGetUserRolesResponse,
  IGetUsersPayload,
  IGetUsersResponse,
  IPATResponse,
  ISaveRolesAndPermissionsPayload,
  ISaveRolesAndPermissionsResponse,
  ISecurityOverview,
  ISessionTimeline,
  IUpdateUserPayload,
  IUpdateUserResponse,
  IUpdateUserAccessControlPayload,
  IUpdateUserAccessControlResponse,
  IRevokeAccessPayload,
  IRevokeAccessResponse,
  IGetSignUpSettingResponse,
  ISaveSignUpSettingPayload,
  ISaveSignUpSettingResponse,
  User,
} from "@blocks-idp/iam/models/user";
import {
  IGetActivitiesPayload,
  IUserActivityResponse,
} from "@blocks-idp/iam/models/activity";
import { UserAccountService } from "./account.service";
import {
  USER_ENDPOINTS,
  ORGANIZATION_ENDPOINTS,
} from "../constants/endpoint.constant";
import { AUTH_ENDPOINTS } from "@/idp/authentication/constants/endpoint.constant";

type ApiUser = User & { OrganizationIds?: string[] };

const toScopedRecord = (
  organizationIds: string[],
  value: Record<string, string[]> | string[] | undefined,
): Record<string, string[]> => {
  if (!value) return {};
  if (!Array.isArray(value)) return { ...value };
  if (organizationIds.length === 0) return {};
  return Object.fromEntries(organizationIds.map((orgId) => [orgId, [...value]]));
};

const normalizeUserFromApi = (raw: ApiUser): User => {
  const organizationIds =
    raw.organizationIds?.length > 0 ? raw.organizationIds : raw.OrganizationIds ?? [];

  return {
    ...raw,
    organizationIds,
    roles: toScopedRecord(
      organizationIds,
      raw.roles as Record<string, string[]> | string[] | undefined,
    ),
    permissions: toScopedRecord(
      organizationIds,
      raw.permissions as Record<string, string[]> | string[] | undefined,
    ),
  };
};

export class UserService {
  constructor(public account: UserAccountService) {}

  getUsers(
    payload: Omit<IGetUsersPayload, "projectKey">,
  ): Promise<IGetUsersResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.GET_USERS, payload);
  }

  getUser(): Promise<{ data: User }> {
    return serviceInstances.idpService.get(`${USER_ENDPOINTS.GET_USER}`, undefined, {
      absoluteUrl: true,
    });
  }

  me(): Promise<{ data: User }> {
    return serviceInstances.idpService.get(`${USER_ENDPOINTS.ME}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserInfo(): Promise<User> {
    return serviceInstances.idpService.get(`${AUTH_ENDPOINTS.USER_INFO}`, undefined, {
      absoluteUrl: true,
    });
  }

  getUserById(payload: IGetUserByIdPayload): Promise<IGetUserByIdResponse> {
    return serviceInstances.idpService
      .get<IGetUserByIdResponse>(`${USER_ENDPOINTS.GET_USER}/${payload.id}`)
      .then((response) => ({
        ...response,
        data: normalizeUserFromApi(response.data as ApiUser),
      }));
  }

  addUser(createPayload: ICreateUserPayload): Promise<ICreateUserResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.CREATE, createPayload);
  }

  isUserExist(
    email: string,
  ): Promise<{ isSuccess: boolean; exists: boolean; organizationIds?: string[] }> {
    return serviceInstances.idpService.get(`${USER_ENDPOINTS.EXISTS}?email=${encodeURIComponent(email)}`);
  }

  updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    const flattenRecord = (value: unknown): string[] => {
      if (!value) return [];
      if (Array.isArray(value)) return value as string[];
      return Object.values(value as Record<string, string[]>).flat();
    };
    const normalized = {
      itemId: payload.itemId,
      firstName: payload.firstName,
      lastName: payload.lastName,
      email: payload.email,
      userName: payload.userName,
      language: payload.language,
      organizationIds: payload.organizationIds,
      roles: flattenRecord(payload.roles),
      permissions: flattenRecord(payload.permissions),
      active: payload.active,
      status: payload.status,
      isVerified: payload.isVerified,
      mfaEnabled: payload.mfaEnabled,
      isMfaVerified: payload.isMfaVerified,
      userMfaType: payload.userMfaType,
      provisioningSource: payload.provisioningSource,
      externalIdentities: payload.externalIdentities,
      userCreationType: payload.userCreationType,
      isMultiOrgEnabled: payload.isMultiOrgEnabled,
      organizations: payload.organizations,
      profileImageId: payload.profileImageId,
      profileImageUrl: payload.profileImageUrl,
    };
    return serviceInstances.idpService.post(`/api/iam/users/${payload.itemId}`, normalized);
  }

  getSignUpSetting(): Promise<IGetSignUpSettingResponse> {
    return serviceInstances.idpService.get(`${ORGANIZATION_ENDPOINTS.GET_SIGNUP_SETTING}`);
  }

  saveSignUpSetting(
    payload: ISaveSignUpSettingPayload,
  ): Promise<ISaveSignUpSettingResponse> {
    return serviceInstances.idpService.post(ORGANIZATION_ENDPOINTS.SAVE_SIGNUP_SETTING, payload);
  }

  saveRolesAndPermissions(
    payload: ISaveRolesAndPermissionsPayload,
  ): Promise<ISaveRolesAndPermissionsResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.SAVE_ROLES_AND_PERMISSIONS, payload);
  }

  updateUserAccessControl(
    payload: IUpdateUserAccessControlPayload,
  ): Promise<IUpdateUserAccessControlResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.ACCESS_CONTROL, payload);
  }

  revokeAccess(payload: IRevokeAccessPayload): Promise<IRevokeAccessResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.REVOKE_ACCESS, payload);
  }

  async getSecurityOverview(): Promise<ISecurityOverview> {
    return serviceInstances.idpService.get<ISecurityOverview>(
      USER_ENDPOINTS.GET_SECURITY_OVERVIEW,
    );
  }

  async getSessionTimeline(sessionId: string): Promise<ISessionTimeline> {
    return serviceInstances.idpService.get<ISessionTimeline>(
      `${USER_ENDPOINTS.REVOKE_SESSION}/${sessionId}`,
    );
  }

  async revokeSession(sessionId: string, reason?: string): Promise<IRevokeSessionResponse> {
    return serviceInstances.idpService.post(
      `${USER_ENDPOINTS.REVOKE_SESSION}/${sessionId}/revoke`,
      reason ? { reason } : {},
    );
  }

  async revokeRefreshToken(tokenId: string, reason?: string): Promise<IRevokeSessionResponse> {
    return serviceInstances.idpService.post(
      USER_ENDPOINTS.REVOKE_REFRESH_TOKEN.replace("{tokenId}", encodeURIComponent(tokenId)),
      reason ? { reason } : {},
    );
  }

  async getActivities(
    payload: IGetActivitiesPayload,
  ): Promise<IUserActivityResponse> {
    const { userId, page, pageSize, sort, filter } = payload;
    const body = {
      page,
      pageSize,
      ...(sort ? { sort } : {}),
      ...(filter ? { filter } : {}),
    };
    return serviceInstances.idpService.post<IUserActivityResponse>(
      USER_ENDPOINTS.GET_ACTIVITIES.replace("{userId}", encodeURIComponent(userId)),
      body,
    );
  }

  async getPats(): Promise<IPATResponse> {
    return serviceInstances.idpService.get(USER_ENDPOINTS.GET_USER_CODES);
  }

  async generatePats(payload: IGeneratePATPayload): Promise<IPATResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.GENERATE_USER_CODE, payload);
  }

  getUserRoles(payload: IGetUserRolesPayload): Promise<IGetUserRolesResponse> {
    return serviceInstances.idpService.get(
      `${USER_ENDPOINTS.GET_USER_ROLES}?Id=${payload.userId}`,
    );
  }

  getUserPermissions(
    payload: IGetUserPermissionsPayload,
  ): Promise<IGetUserPermissionsResponse> {
    return serviceInstances.idpService.get(
      `${USER_ENDPOINTS.GET_USER_PERMISSIONS}?Id=${payload.userId}`,
    );
  }

  accountDeactivate(
    payload: IAccountResendActivationPayload,
  ): Promise<IAccountResendActivationResponse> {
    return serviceInstances.idpService.post(USER_ENDPOINTS.DEACTIVATE, payload);
  }
}

export const userService = new UserService(new UserAccountService());
