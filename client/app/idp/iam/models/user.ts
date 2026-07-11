import { IPermission } from "./permission";
import { IRole } from "./role";
import { IRefreshTokenRotation } from "./refresh-token";

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
  OrganizationsRoles?: Record<string, string[]>;
  OrganizationsPermissions?: Record<string, string[]>;
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
  organizationIds?: string[];
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
export interface IUpdateUserAccessControlPayload {
  userId: string;
  roles: string[];
  permissions: string[];
  organizationId: string;
}
export interface IUpdateUserAccessControlResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface IRevokeAccessPayload {
  userId: string;
  organizationId: string;
}
export interface IRevokeAccessResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IRevokeSessionResponse {
  sessionId: string;
  alreadyRevoked: boolean;
  revokedAt: string;
  reason: string | null;
  revokedRefreshTokens: number;
  warnings: string[];
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

// Interface for the data we pass to the InviteUser modal
export interface EditUserData {
  itemId: string;
  firstName: string;
  lastName: string | null;
  email: string;
  phoneNumber: string | null;
  salutation: string;
}

export interface IAppSession {
  tokenId: string;
  sessionId: string;
  userId: string;
  tenantId: string;
  organizationId?: string | null;
  clientId?: string | null;
  grantType?: string | null;
  ipAddresses?: string | null;
  userAgent?: string | null;
  deviceName?: string | null;
  deviceModel?: string | null;
  operatingSystem?: string | null;
  browser?: string | null;
  issuedUtc?: string;
  slidingExpiry?: string;
  absoluteExpiry?: string;
  isActive: boolean;
  impersonated: boolean;
  impersonationId?: string | null;
}

export interface ISessionGroup {
  sessionId: string;
  tenantId: string;
  userId?: string | null;
  createdAt?: string;
  lastActivityAt?: string;
  isCurrent: boolean;
  apps: IAppSession[];
}

export interface IIdpSessionAccount {
  userId?: string | null;
  tenantId?: string | null;
  displayName?: string | null;
  loginAt?: string;
}

export interface IIdpSession {
  sessionId?: string | null;
  tenantId?: string | null;
  accounts: IIdpSessionAccount[];
  ipAddress?: string | null;
  createdAt?: string;
  lastActivityAt?: string;
  idleExpiry?: string;
  absoluteExpiry?: string;
  isRevoked: boolean;
}

export interface IRevokedAccessToken {
  jti?: string | null;
  revokedAt?: string | null;
  reason?: string | null;
}

export interface IRefreshTokenStatus {
  tokenId?: string | null;
  isRevoked: boolean;
  issuedAt?: string | null;
  absoluteExpiry?: string | null;
  revokedAt?: string | null;
  revokeReason?: string | null;
}

export interface ISessionTimeline {
  sessionId?: string | null;
  session?: ISessionGroup | null;
  refreshTokenStatus?: IRefreshTokenStatus | null;
  revokedAccessTokens: IRevokedAccessToken[];
  lifecycle: IAuthHistoryEvent[];
  rotations: IRefreshTokenRotation[];
}

export interface IAuthHistoryEvent {
  event?: string | null;
  actionBy?: string | null;
  deviceName?: string | null;
  deviceType?: string | null;
  deviceInformation?: {
    browser?: string;
    os?: string;
    device?: string;
  } | null;
  ipAddresses?: string | null;
  sessionId?: string | null;
  tenantId?: string | null;
  clientId?: string | null;
  correlationId?: string | null;
  outcome?: string | null;
  reasonCode?: string | null;
  riskLevel?: string | null;
  createdDate?: string;
}

export interface ISecurityOverview {
  currentSessionId?: string | null;
  sessionGroups: ISessionGroup[];
  idpSession: IIdpSession | null;
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
