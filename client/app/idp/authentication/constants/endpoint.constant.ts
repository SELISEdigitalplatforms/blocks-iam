// ─── Subpaths ─────────────────────────────────────────────────────────────────

const AUTH_OIDC_SUBPATH = "/oidc";

// ─── Auth endpoints (backend: /api/auth/*) ──────────────────────────────────

export const AUTH_ENDPOINTS = {
  USER_INFO: `/api/idp/UserInfo`,
  LOGIN: `/api/auth/login`,
  RECOVER: `/api/auth/recover`,
  RESET_PASSWORD: `/api/auth/reset-password`,
  CHANGE_PASSWORD: `/api/auth/change-password`,
  REFRESH: `/api/auth/refresh`,
  LOGOUT: `/api/auth/logout`,
  IMPERSONATE: `/api/auth/impersonate`,
  STOP_IMPERSONATION: `/api/auth/impersonation/stop`,
  SOCIAL_AUTHORIZE: `/api/auth/social/authorize`,
  SOCIAL_CALLBACK: `/api/auth/social/callback`,
  SOCIAL_LOGIN: `/api/auth/social/callback`,
  OIDC_TOKEN: `/api/oidc/token`,
  OIDC_LOGIN: `/api/oidc/login`,
  OIDC_LOGIN_SELECT_ACCOUNT: `/api/oidc/login/select-account`,
  TOKEN_EXCHANGE: `/api/token/exchange`,
  GET_LOGIN_OPTIONS: `/api/auth/login-options`,
  SIGNUP: `/api/auth/signup`,
  ACTIVATE_ACCOUNT: `/api/iam/users/activate`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `/api/oidc-clients`,
  GET_OIDC_CLIENT: `/api/oidc-clients`, // append /{clientId} at call site
  SAVE_OIDC_CLIENT: `/api/oidc-clients`,
  DELETE_OIDC_CLIENT: `/api/oidc-clients`, // append /{clientId} at call site
  OIDC_TOKEN: `/api${AUTH_OIDC_SUBPATH}/token`,
} as const;

// ─── Auth configuration endpoints ───────────────────────────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `/api/iam/config`,
  UPDATE_CONFIG: `/api/iam/config`,
} as const;

// ─── Auth client credentials endpoints ──────────────────────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `/api/oidc-clients`,
  SAVE_CLIENT_CREDENTIAL: `/api/oidc-clients`,
  DELETE_CLIENT_CREDENTIAL: `/api/oidc-clients`,
} as const;

// ─── IAM Management endpoints ──────────────────────────────────────────────

export const IAM_ENDPOINTS = {
  GET_CONFIG: `/api/iam/config`,
  UPDATE_CONFIG: `/api/iam/config`,
  USERS: `/api/iam/users`,
  ORGANIZATIONS: `/api/iam/organizations`,
  ROLES: `/api/iam/roles`,
  PERMISSIONS: `/api/iam/permissions`,
} as const;

// ─── SSO provider management endpoints ──────────────────────────────────────

export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `/auth/sso/credentials`,
  GET_SSO_CREDENTIAL: `/auth/sso/credential`,
  SAVE_SSO_CREDENTIAL: `/auth/sso/credential/save`,
  DELETE_SSO_CREDENTIAL: `/auth/sso/credential/delete`,
  UPDATE_STATUS: `/auth/sso/credential/status`,
} as const;

export const IMPERSONATE_ENDPOINTS = {
  IMPERSONATE: `/api/auth/impersonate`,
  STOP_IMPERSONATION: `/api/auth/impersonation/stop`,
} as const;

// ─── Identity Provider endpoints (identity-provider.service) ─────────────────

export const EXECUTION_CONTEXT_ENDPOINTS = {
  CONTEXT: `/api/auth/context`,
};
