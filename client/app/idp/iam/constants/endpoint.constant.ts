// ─── Subpaths ─────────────────────────────────────────────────────────────────

const IAM_SUBPATH = "/Iam";
const AUTH_SUBPATH = "/Authentication";
const IAM_CONFIG_SUBPATH = "/IAM";

// ─── User endpoints (user.service) ──────────────────────────────────────────

export const USER_ENDPOINTS = {
  GET_USERS: `/api${IAM_SUBPATH}/users`,
  GET_USER: `/api${IAM_SUBPATH}/user`,
  CREATE: `/api${IAM_SUBPATH}/Create`,
  UPDATE: `/api${IAM_SUBPATH}/Update`,
  GET_SIGNUP_SETTING: `/api${IAM_SUBPATH}/GetSignUpSetting`,
  SAVE_SIGNUP_SETTING: `/api${IAM_SUBPATH}/SaveSignUpSetting`,
  SAVE_ROLES_AND_PERMISSIONS: `/api${IAM_SUBPATH}/SaveRolesAndPermissions`,
  GET_SESSIONS: `/api${IAM_SUBPATH}/GetSessions`,
  GET_HISTORIES: `/api${IAM_SUBPATH}/GetHistories`,
  GET_USER_CODES: `/api${AUTH_SUBPATH}/GetUserCodes`,
  GENERATE_USER_CODE: `/api${AUTH_SUBPATH}/GenerateUserCode`,
  GET_USER_ROLES: `/api${IAM_SUBPATH}/GetUserRoles`,
  GET_USER_PERMISSIONS: `/api${IAM_SUBPATH}/GetUserPermissions`,
  DEACTIVATE: `/api${IAM_SUBPATH}/Deactivate`,
} as const;

// ─── Account endpoints (account.service) ────────────────────────────────────

export const ACCOUNT_ENDPOINTS = {
  ACTIVATE: `/api${IAM_SUBPATH}/Activate`,
  RESEND_ACTIVATION: `/api${IAM_SUBPATH}/ResendActivation`,
  RECOVER: `/api${IAM_SUBPATH}/Recover`,
  RESET_PASSWORD: `/api${IAM_SUBPATH}/ResetPassword`,
  VALIDATE_ACTIVATION_CODE: `/api${IAM_SUBPATH}/ValidateActivationCode`,
} as const;

// ─── Role endpoints (role.service) ──────────────────────────────────────────

export const ROLE_ENDPOINTS = {
  GET_ROLES: `/api${IAM_SUBPATH}/roles`,
  GET_ROLE: `/api${IAM_SUBPATH}/role`,
  CREATE_ROLE: `/api${IAM_SUBPATH}/roles/create`,
  UPDATE_ROLE: `/api${IAM_SUBPATH}/roles/update`,
  SET_ROLES: `/api${IAM_SUBPATH}/roles/assign`,
} as const;

// ─── Permission endpoints (permission.service) ─────────────────────────────

export const PERMISSION_ENDPOINTS = {
  GET_PERMISSIONS: `/api${IAM_SUBPATH}/GetPermissions`,
  GET_PERMISSION: `/api${IAM_SUBPATH}/GetPermission`,
  GET_PERMISSIONS_GROUP_BY_SEVERITY: `/api${IAM_SUBPATH}/GetPermissionsGroupBySeverity`,
  CREATE_PERMISSION: `/api${IAM_SUBPATH}/CreatePermission`,
  UPDATE_PERMISSION: `/api${IAM_SUBPATH}/UpdatePermission`,
  GET_RESOURCE_GROUPS: `/api${IAM_SUBPATH}/GetResourceGroups`,
} as const;

// ─── Organization endpoints (organization.service) ─────────────────────────

export const ORGANIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: `/api${IAM_SUBPATH}/GetOrganizations`,
  GET_ORGANIZATION: `/api${IAM_SUBPATH}/GetOrganization`,
  SAVE_ORGANIZATION: `/api${IAM_SUBPATH}/SaveOrganization`,
  GET_ORGANIZATION_CONFIG: `/api${IAM_SUBPATH}/GetOrganizationConfig`,
  SAVE_ORGANIZATION_CONFIG: `/api${IAM_SUBPATH}/SaveOrganizationConfig`,
} as const;

// ─── IAM configuration endpoints (configuration.service) ───────────────────

export const IAM_CONFIGURATION_ENDPOINTS = {
  GET: `/api${IAM_CONFIG_SUBPATH}/Get`,
  SAVE: `/api${IAM_CONFIG_SUBPATH}/Save`,
} as const;
