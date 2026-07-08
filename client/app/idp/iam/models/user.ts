import { IPermission } from "./permission";
import { IRole } from "./role";

export interface User {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  language: string;
  salutation: string;
  firstName: string;
  lastName: string;
  email: string;
  userName: string;
  phoneNumber: string;
  organizationIds: string[];
  lastUsedOrganizationId: string | null;
  roles: Record<string, string[]>;
  permissions: Record<string, string[]>;
  active: boolean;
  status: number;
  statusReason: string | null;
  deactivatedAtUtc: string | null;
  isVarified: boolean;
  isVerified: boolean;
  emailVerifiedAtUtc: string | null;
  phoneVerifiedAtUtc: string | null;
  profileImageUrl: string;
  profileImageId: string;
  mfaEnabled: boolean;
  isMfaVerified: boolean;
  userMfaType: number;
  lastLoggedInTime: string;
  lastLoggedInDeviceInfo: string;
  logInCount: number;
  firstLoggedInTime: string;
  provisioningSource: number;
  externalIdentities: unknown[];
  userCreationType: number;
  department: string | null;
  employeeId: string | null;
  isMultiOrgEnabled: boolean;
  organizations: IMembership[];
}

export interface IMembership {
  organizationId: string;
  roles: string[];
  permissions: string[];
}
export interface IGetUsersPayload {
  page: number;
  pageSize: number;
  sort?: {
    property: string;
    isDescending: boolean;
  };
  filter?: {
    email: string;
    name: string;
    organizationId?: string;
  };
  projectKey: string;
}
export interface IGetUsersResponse {
  errors: unknown;
  data: User[];
  totalCount: number;
}

export interface IGetUserByIdPayload {
  id: string;
  projectKey: string;
}
export interface IGetUserByIdResponse {
  data: User;
  errors: unknown;
}

export interface ICreateUserPayload {
  email: string;
  firstName: string;
  lastName: string;
  userPassType: number;
  userCreationType: number;
  platform: string;
  projectKey: string;
  organizationId?: string;
}
export interface ICreateUserResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string | null;
}

export interface IUpdateUserPayload {
  itemId: string;
  firstName?: string;
  lastName?: string;
  email?: string;
  userName?: string;
  language?: string;
  organizationIds?: string[];
  roles?: string[];
  permissions?: string[];
  active?: boolean;
  status?: number;
  isVerified?: boolean;
  mfaEnabled?: boolean;
  isMfaVerified?: boolean;
  userMfaType?: number;
  provisioningSource?: number;
  externalIdentities?: unknown[];
  userCreationType?: number;
  isMultiOrgEnabled?: boolean;
  organizations?: string[];
  profileImageId?: string | null;
  profileImageUrl?: string | null;
}

export interface IUpdateUserResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string | null;
}

export interface ISaveRolesAndPermissionsPayload {
  userId: string;
  roles?: string[];
  permissions?: string[];
  projectKey: string;
}
export interface ISaveRolesAndPermissionsResponse {
  errors: unknown | null;
  isSuccess: boolean;
  itemId: string;
}
export interface IGetSessionPayload {
  page: number;
  pageSize: number;
  filter: { UserId: string };
  projectKey: string;
}
export interface IGetHistoriesPayload {
  page: number;
  pageSize: number;
  filter: { UserId: string };
  projectKey: string;
}

export interface IGeneratePATPayload {
  note?: string;
  codeTtlInMinute: number;
  clientId: string;
}

export interface IGetUserRolesPayload {
  userId: string;
}
export interface IGetUserRolesResponse {
  totalCount: number;
  data: IRole[];
  errors: unknown | null;
}

export interface IGetUserPermissionsPayload {
  userId: string;
}
export interface IGetUserPermissionsResponse {
  errors: unknown | null;
  totalCount: number;
  data: IPermission[];
}

export interface UserDetailsDevicesData {
  site: string;
  device: string;
  noRefreshTokens: string | number;
  lastAccessOn: string;
}

export interface UserDetailsHistoryData {
  event: string;
  time: string;
  site: string | number;
  accessFrom: UserAccessFromData;
}

export interface UserAccessFromData {
  ip: string;
  location: string;
}

// Interface for the data we pass to the InviteUser modal
export interface EditUserData {
  itemId: string;
  firstName: string;
  lastName: string | null;
  email: string;
  phoneNumber: string | null;
  salutation: string;
}

interface DeviceInformation {
  Browser: string;
  OS: string;
  Device: string;
  Brand: string;
  Model: string;
}

export interface IHistories {
  _id: string;
  CreatedDate: string;
  LastUpdatedDate: string;
  CreatedBy: string;
  LastUpdatedBy: string;
  OrganizationIds: string[];
  Tags: string[];
  Event: string;
  ActionBy: string;
  IpAddresses: string;
  DeviceInformation: DeviceInformation;
}

export interface IHistoriesResponse {
  totalCount: number;
  data: IHistories[];
  errors: unknown;
}

export interface IDeviceSession {
  RefreshToken: string;
  TenantId: string;
  IssuedUtc: Date;
  ExpiresUtc: Date;
  UserId: string;
  IpAddresses: string;
  DeviceInformation: DeviceInformation;
  CreateDate: Date;
  UpdateDate: Date;
  IsActive: boolean;
  _id: string;
}

export interface IDeviceSessionResponse {
  totalCount: number;
  data: IDeviceSession[];
  errors: unknown;
}

export interface IPATResponse {
  note: string;
  itemId: string;
  createdDate: Date;
  expiryDate: Date;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  code: string;
  userId: string;
  clientId: string;
}

export const status = [
  {
    value: "Active",
    label: "Active",
  },
  {
    value: "Inactive",
    label: "Inactive",
  },
  {
    value: "Verified",
    label: "Verified",
  },
];

export interface IAccountActivationPayload {
  code: string;
  password: string;
  firstname?: string;
  lastname?: string;
  captchaCode?: string;
  mailPurpose?: string;
  preventPostEvent: boolean;
  tenantId?: string;
}

export interface IAccountActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IAccountResendActivationPayload {
  userId: string;
  tenantId?: string;
}
export interface IAccountResendActivationResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountRecoverPayload {
  email: string;
  captchaCode?: string;
  mailPurpose?: string;
  tenantId?: string;
}
export interface IAccountRecoverResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IAccountResetPasswordPayload {
  code: string;
  password: string;
  captchaCode?: string;
  logoutFromAllDevices?: boolean;
  tenantId?: string;
}
export interface IAccountResetPasswordResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IChangePasswordPayload {
  oldPassword: string;
  newPassword: string;
  confirmNewPassword: string;
}

export interface IChangePasswordResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IActivationCodeValidationPayload {
  activationCode: string;
  tenantId?: string;
}

export interface IActivationCodeValidationResponse {
  errors: unknown | null;
  isSuccess: boolean;
  userId: string | null;
}

export interface ISaveSignUpSettingPayload {
  isEmailPasswordSignUpEnabled: boolean;
  isSSoSignUpEnabled: boolean;
  defaultRolesForNewUserOnSignUp: string[];
  defaultPermissionsForNewUserOnSignUp: string[];
}

export interface ISaveSignUpSettingResponse {
  errors: unknown;
  isSuccess: boolean;
  itemId: string;
}

export interface IGetSignUpSettingResponse {
  isSignUpEnable: boolean;
  isEmailPasswordSignUpEnabled: boolean;
  isSSoSignUpEnabled: boolean;
  defaultRolesForNewUser: string[];
  defaultPermissionsForNewUser: string[];
}
