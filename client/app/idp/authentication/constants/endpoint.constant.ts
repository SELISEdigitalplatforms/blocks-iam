import { API_BASES } from "@/constants/endpoint.constant";

// ─── Auth endpoints (backend: /api/auth/*) ──────────────────────────────────

export const AUTH_ENDPOINTS = {
  LOGIN: `${API_BASES.IDP}/auth/login`,
  RECOVER: `${API_BASES.IDP}/auth/recover`,
  RESET_PASSWORD: `${API_BASES.IDP}/auth/reset-password`,
  CHANGE_PASSWORD: `${API_BASES.IDP}/auth/change-password`,
  REFRESH: `${API_BASES.IDP}/auth/refresh`,
  LOGOUT: `${API_BASES.IDP}/auth/logout`,
  IMPERSONATE: `${API_BASES.IDP}/auth/impersonate`,
  STOP_IMPERSONATION: `${API_BASES.IDP}/auth/impersonation/stop`,
  SOCIAL_AUTHORIZE: `${API_BASES.IDP}/auth/social/authorize`,
  SOCIAL_CALLBACK: `${API_BASES.IDP}/auth/social/callback`,
  OIDC_TOKEN: `${API_BASES.IDP}/oidc/token`,
  OIDC_LOGIN: `${API_BASES.IDP}/oidc/login`,
  OIDC_LOGIN_SELECT_ACCOUNT: `${API_BASES.IDP}/oidc/login/select-account`,
  OIDC_LOGIN_PAGE: `${API_BASES.IDP}/auth/oidc/login-page`,
  OIDC_SOCIAL_AUTHORIZE: `${API_BASES.IDP}/auth/oidc/social/authorize`,
  GET_LOGIN_OPTIONS: `${API_BASES.IDP}/auth/login-options`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${API_BASES.IDP}/oidc-clients`,
  GET_OIDC_CLIENT: `${API_BASES.IDP}/oidc-clients`,   // append /{clientId} at call site
  SAVE_OIDC_CLIENT: `${API_BASES.IDP}/oidc-clients`,
  DELETE_OIDC_CLIENT: `${API_BASES.IDP}/oidc-clients`, // append /{clientId} at call site
} as const;

// ─── Auth configuration endpoints ───────────────────────────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${API_BASES.IDP}/iam/config`,
  UPDATE_CONFIG: `${API_BASES.IDP}/iam/config`,
} as const;

// ─── Auth client credentials endpoints ──────────────────────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `${API_BASES.IDP}/oidc-clients`,
  SAVE_CLIENT_CREDENTIAL: `${API_BASES.IDP}/oidc-clients`,
  DELETE_CLIENT_CREDENTIAL: `${API_BASES.IDP}/oidc-clients`,
} as const;
// ─── IAM Management endpoints ──────────────────────────────────────────────

export const IAM_ENDPOINTS = {
  GET_CONFIG: `${API_BASES.IDP}/iam/config`,
  UPDATE_CONFIG: `${API_BASES.IDP}/iam/config`,
  USERS: `${API_BASES.IDP}/iam/users`,
  ORGANIZATIONS: `${API_BASES.IDP}/iam/organizations`,
  ROLES: `${API_BASES.IDP}/iam/roles`,
  PERMISSIONS: `${API_BASES.IDP}/iam/permissions`,
} as const;


