// ─── Subpaths ─────────────────────────────────────────────────────────────────

const IAM_SUBPATH = "/iam";
const AUTH_SUBPATH = "/auth";

// ─── User endpoints (user.service) ──────────────────────────────────────────

export const USER_ENDPOINTS = {
  GET_USERS: `/api${IAM_SUBPATH}/users`,
  GET_USER: `/api${IAM_SUBPATH}/users`,
  ME: `/api${IAM_SUBPATH}/me`,

  CREATE: `/api${IAM_SUBPATH}/users/create`,
  UPDATE: `/api${IAM_SUBPATH}/users/update`,
  DEACTIVATE: `/api${IAM_SUBPATH}/users/deactivate`,
UPDATE_ACCOUNT: `/api${IAM_SUBPATH}/account/update`,
  ACCESS_CONTROL: `/api${IAM_SUBPATH}/users/access`,
  ACCESS_CONTROL_V1: `/api${IAM_SUBPATH}/users/access-control`,
  REVOKE_ACCESS: `/api${IAM_SUBPATH}/users/revoke-access`,
  EXISTS: `/api${IAM_SUBPATH}/users/exists`,
  GET_ACCOUNTS: `/api${IAM_SUBPATH}/accounts`,
  GET_ACCOUNT: `/api${IAM_SUBPATH}/account`,
  GET_ACCOUNT_ROLES: `/api${IAM_SUBPATH}/account/roles`,
  GET_ACCOUNT_PERMISSIONS: `/api${IAM_SUBPATH}/account/permissions`,
  SAVE_ROLES_AND_PERMISSIONS: `/api${IAM_SUBPATH}/roles-permissions`,
  GET_SESSIONS: `/api/security/sessions`,
  REVOKE_SESSION: `/api/security/sessions`, // POST {base}/{sessionId}/revoke
  GET_ACTIVITIES: `/api${IAM_SUBPATH}/activity/{userId}`,
  GET_SESSION_REFRESH_TOKENS: `/api/security/sessions/{sessionId}/refresh-tokens`,
  GET_USER_CODES: `/api${AUTH_SUBPATH}/GetUserCodes`,
  GENERATE_USER_CODE: `/api${AUTH_SUBPATH}/GenerateUserCode`,
  GET_USER_ROLES: `/api${IAM_SUBPATH}/user/roles`,
  GET_USER_PERMISSIONS: `/api${IAM_SUBPATH}/user/permissions`,
  IS_EMAIL_AVAILABLE: `/api${IAM_SUBPATH}/email/available`,
} as const;

// ─── Account endpoints (account.service) ────────────────────────────────────

export const ACCOUNT_ENDPOINTS = {
  ACTIVATE: `/api${AUTH_SUBPATH}/activate`,
  RESEND_ACTIVATION: `/api${AUTH_SUBPATH}/resend-activation`,
  RECOVER: `/api${AUTH_SUBPATH}/recover`,
  RESET_PASSWORD: `/api${AUTH_SUBPATH}/reset-password`,
  VALIDATE_ACTIVATION_CODE: `/api${AUTH_SUBPATH}/validate-activation`,
  CHANGE_PASSWORD: `/api${AUTH_SUBPATH}/change-password`,
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
  GET_PERMISSIONS: `/api${IAM_SUBPATH}/permissions`,
  GET_PERMISSION: `/api${IAM_SUBPATH}/permission`,
  GET_PERMISSIONS_GROUP_BY_SEVERITY: `/api${IAM_SUBPATH}/permissions/by-severity`,
  CREATE_PERMISSION: `/api${IAM_SUBPATH}/permissions/create`,
  UPDATE_PERMISSION: `/api${IAM_SUBPATH}/permissions/update`,
  GET_RESOURCE_GROUPS: `/api${IAM_SUBPATH}/resource-groups`,
} as const;

// ─── Organization endpoints (organization.service) ─────────────────────────

export const ORGANIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: `/api${IAM_SUBPATH}/organizations`,
  GET_ORGANIZATION: `/api${IAM_SUBPATH}/organizations`,
  CREATE_ORGANIZATION: `/api${IAM_SUBPATH}/organizations/create`,
  SAVE_ORGANIZATION: `/api${IAM_SUBPATH}/organizations`,
  UPDATE_ORGANIZATION: `/api${IAM_SUBPATH}/organizations`,
  GET_ORGANIZATION_CONFIG: `/api${IAM_SUBPATH}/organizations/config`,
  SAVE_ORGANIZATION_CONFIG: `/api${IAM_SUBPATH}/organizations/config`,
  GET_MY_ORGANIZATIONS: `/api${IAM_SUBPATH}/organizations/my`,
  GET_SIGNUP_SETTING: `/api${IAM_SUBPATH}/signup-settings`,
  SAVE_SIGNUP_SETTING: `/api${IAM_SUBPATH}/signup-settings`,
} as const;

// ─── IAM configuration endpoints (configuration.service) ───────────────────

export const IAM_CONFIGURATION_ENDPOINTS = {
  GET: `/api${IAM_SUBPATH}/config`,
  SAVE: `/api${IAM_SUBPATH}/config`,
} as const;
