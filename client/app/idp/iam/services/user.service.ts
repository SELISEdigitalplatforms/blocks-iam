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

  async updateUser(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    const current = await this.getUserById({ id: payload.itemId, projectKey: "" });
    const flattened = flattenRolesAndPermissions(current.data, payload);
    // The /users/{id} endpoint treats the body as a full record replacement —
    // omitting a field on the request wipes it on the server. Fetch the
    // latest server-side record and merge the requested changes on top so
    // unrelated fields (image, name, roles, MFA flags, etc.) survive the
    // update.
    const body = mergeUserUpdate(current.data, payload, flattened);
    return serviceInstances.idpService.post(
      `${USER_ENDPOINTS.UPDATE}/${payload.itemId}`,
      body,
    );
  }

  async updateMe(payload: IUpdateUserPayload): Promise<IUpdateUserResponse> {
    const current = await this.me();
    const flattened = flattenRolesAndPermissions(current.data, payload);
    // /api/iam/me behaves the same as /api/iam/users/{id} — it overwrites
    // whatever fields aren't in the body. Read the current record, apply
    // the requested changes, and POST the merged record.
    const body = mergeUserUpdate(current.data, payload, flattened);
    return serviceInstances.idpService.post(USER_ENDPOINTS.UPDATE_ME, body);
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

type FlattenedRolesAndPermissions = {
  organizationIds?: string[];
  roles?: string[];
  permissions?: string[];
};

const flattenRolesAndPermissions = (
  current: User | undefined,
  payload: IUpdateUserPayload,
): FlattenedRolesAndPermissions => {
  const result: FlattenedRolesAndPermissions = {};
  const payloadHasRolesOrOrgs =
    payload.roles !== undefined ||
    payload.permissions !== undefined ||
    payload.organizations !== undefined ||
    payload.organizationIds !== undefined;

  if (!payloadHasRolesOrOrgs && !current) return result;

  const organizationIds =
    payload.organizationIds !== undefined
      ? payload.organizationIds
      : (current?.organizationIds ?? []);

  if (payload.organizationIds !== undefined) {
    result.organizationIds = payload.organizationIds;
  }

  if (payload.roles !== undefined) {
    result.roles = Array.isArray(payload.roles)
      ? payload.roles
      : Object.values(payload.roles as Record<string, string[]>).flat();
  } else if (current?.roles) {
    result.roles = Object.values(current.roles).flat();
  }

  if (payload.permissions !== undefined) {
    result.permissions = Array.isArray(payload.permissions)
      ? payload.permissions
      : Object.values(payload.permissions as Record<string, string[]>).flat();
  } else if (current?.permissions) {
    result.permissions = Object.values(current.permissions).flat();
  }

  if (payload.roles !== undefined || payload.permissions !== undefined) {
    result.organizationIds = organizationIds;
  }

  return result;
};

const mergeUserUpdate = (
  current: User,
  payload: IUpdateUserPayload,
  flattened: FlattenedRolesAndPermissions,
): Record<string, unknown> => {
  // Send every known field of the current record so the server doesn't
  // wipe untouched ones (profileImage, name, MFA flags, etc.). The fields
  // in `payload` (and the flattened role/permission forms) override.
  const body: Record<string, unknown> = {
    ...current,
    itemId: payload.itemId,
  };
  for (const [key, value] of Object.entries(payload)) {
    if (value !== undefined) body[key] = value;
  }
  if (flattened.organizationIds !== undefined) {
    body.organizationIds = flattened.organizationIds;
  }
  if (flattened.roles !== undefined) body.roles = flattened.roles;
  if (flattened.permissions !== undefined) body.permissions = flattened.permissions;
  return body;
};