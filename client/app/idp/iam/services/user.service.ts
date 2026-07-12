import { serviceInstances } from "@/lib/http-client";
import { parseMongoDBString } from "@/lib/utils";
import {
  IAccountResendActivationPayload,
  IAccountResendActivationResponse,
  ICreateUserPayload,
  ICreateUserResponse,
  IGetUserByIdPayload,
  IGetUserByIdResponse,
  IGetUserPermissionsPayload,
  IGetUserPermissionsResponse,
  IGetUserRolesPayload,
  IGetUserRolesResponse,
  IGetUsersPayload,
  IGetUsersResponse,
  ISaveRolesAndPermissionsPayload,
  ISaveRolesAndPermissionsResponse,
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
  ): Promise<{ userId?: string; organizationIds?: string[] }> {
    return serviceInstances.idpService.get(`${USER_ENDPOINTS.EXISTS}?email=${encodeURIComponent(email)}`);
  }

  updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    const flattenRecord = (value: unknown): string[] => {
      if (!value) return [];
      if (Array.isArray(value)) return value as string[];
      return Object.values(value as Record<string, string[]>).flat();
    };
    // The update endpoint treats the request body as a partial — only fields
    // explicitly present on `payload` are forwarded. Undefined keys are
    // dropped so callers can PATCH a single field without overwriting the
    // rest of the server-side record.
    const normalized: Record<string, unknown> = {
      itemId: payload.itemId,
      firstName: payload.firstName,
      lastName: payload.lastName,
    };
    if (payload.email !== undefined) normalized.email = payload.email;
    if (payload.userName !== undefined) normalized.userName = payload.userName;
    if (payload.language !== undefined) normalized.language = payload.language;
    if (payload.organizationIds !== undefined) {
      normalized.organizationIds = payload.organizationIds;
    }
    if (payload.roles !== undefined) {
      normalized.roles = flattenRecord(payload.roles);
    }
    if (payload.permissions !== undefined) {
      normalized.permissions = flattenRecord(payload.permissions);
    }
    if (payload.active !== undefined) normalized.active = payload.active;
    if (payload.status !== undefined) normalized.status = payload.status;
    if (payload.isVerified !== undefined) normalized.isVerified = payload.isVerified;
    if (payload.mfaEnabled !== undefined) normalized.mfaEnabled = payload.mfaEnabled;
    if (payload.isMfaVerified !== undefined) {
      normalized.isMfaVerified = payload.isMfaVerified;
    }
    if (payload.userMfaType !== undefined) normalized.userMfaType = payload.userMfaType;
    if (payload.provisioningSource !== undefined) {
      normalized.provisioningSource = payload.provisioningSource;
    }
    if (payload.externalIdentities !== undefined) {
      normalized.externalIdentities = payload.externalIdentities;
    }
    if (payload.userCreationType !== undefined) {
      normalized.userCreationType = payload.userCreationType;
    }
    if (payload.isMultiOrgEnabled !== undefined) {
      normalized.isMultiOrgEnabled = payload.isMultiOrgEnabled;
    }
    if (payload.organizations !== undefined) normalized.organizations = payload.organizations;
    if (payload.profileImageId !== undefined) {
      normalized.profileImageId = payload.profileImageId;
    }
    if (payload.profileImageUrl !== undefined) {
      normalized.profileImageUrl = payload.profileImageUrl;
    }
    return serviceInstances.idpService.post(USER_ENDPOINTS.UPDATE, normalized);
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