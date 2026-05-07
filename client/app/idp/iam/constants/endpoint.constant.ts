import { API_BASES } from "@/constants/endpoint.constant";

// ─── Base paths ──────────────────────────────────────────────────────────────

const IAM_BASE = `${API_BASES.IDP}/iam`;

// ─── User endpoints (user.service) ──────────────────────────────────────────

export const USER_ENDPOINTS = {
  GET_USERS: `${IAM_BASE}/users`,
  GET_USER: `${IAM_BASE}/account`,
  CREATE: `${IAM_BASE}/users/create`,
  UPDATE: `${IAM_BASE}/users/update`,
  GET_SIGNUP_SETTING: `${IAM_BASE}/config`,
  SAVE_SIGNUP_SETTING: `${IAM_BASE}/config`,
  SAVE_ROLES_AND_PERMISSIONS: `${IAM_BASE}/roles/assign`,
  GET_SESSIONS: `${IAM_BASE}/sessions`,
  GET_HISTORIES: `${IAM_BASE}/history`,
  GET_USER_ROLES: `${IAM_BASE}/roles`,
  GET_USER_PERMISSIONS: `${IAM_BASE}/permissions`,
  DEACTIVATE: `${IAM_BASE}/users/deactivate`,
} as const;

// ─── Account endpoints (account.service) ────────────────────────────────────

export const ACCOUNT_ENDPOINTS = {
  ACTIVATE: `${IAM_BASE}/activate`,
  RESEND_ACTIVATION: `${IAM_BASE}/resend-activation`,
  RECOVER: `${API_BASES.IDP}/auth/recover`,
  RESET_PASSWORD: `${API_BASES.IDP}/auth/reset-password`,
  VALIDATE_ACTIVATION_CODE: `${IAM_BASE}/validate-activation`,
} as const;

// ─── Role endpoints (role.service) ──────────────────────────────────────────

export const ROLE_ENDPOINTS = {
  GET_ROLES: `${IAM_BASE}/roles`,
  GET_ROLE: `${IAM_BASE}/role`,
  CREATE_ROLE: `${IAM_BASE}/roles/create`,
  UPDATE_ROLE: `${IAM_BASE}/roles/update`,
  SET_ROLES: `${IAM_BASE}/roles/assign`,
} as const;

// ─── Permission endpoints (permission.service) ─────────────────────────────

export const PERMISSION_ENDPOINTS = {
  GET_PERMISSIONS: `${IAM_BASE}/permissions`,
  GET_PERMISSION: `${IAM_BASE}/permission`,
  GET_PERMISSIONS_GROUP_BY_SEVERITY: `${IAM_BASE}/permissions/by-severity`,
  CREATE_PERMISSION: `${IAM_BASE}/permissions/create`,
  UPDATE_PERMISSION: `${IAM_BASE}/permissions/update`,
  GET_RESOURCE_GROUPS: `${IAM_BASE}/resource-groups`,
} as const;

// ─── Organization endpoints (organization.service) ─────────────────────────

export const ORGANIZATION_ENDPOINTS = {
  GET_ORGANIZATIONS: `${IAM_BASE}/organizations`,
  GET_ORGANIZATION: `${IAM_BASE}/organization`,
  SAVE_ORGANIZATION: `${IAM_BASE}/organization/update`,
  GET_ORGANIZATION_CONFIG: `${IAM_BASE}/config`,
  SAVE_ORGANIZATION_CONFIG: `${IAM_BASE}/config`,
} as const;

// ─── IAM configuration endpoints (configuration.service) ───────────────────

export const IAM_CONFIGURATION_ENDPOINTS = {
  GET: `${IAM_BASE}/config`,
  SAVE: `${IAM_BASE}/config`,
} as const;
