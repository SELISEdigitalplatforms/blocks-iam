// Inline base path (empty — HttpClient prepends BLOCKS_API_BASE_URL at runtime)
const IDP_BASE = "";

// ─── Auth endpoints (backend: /api/auth/*) ──────────────────────────────────

export const AUTH_ENDPOINTS = {
  LOGIN: `${IDP_BASE}/api/auth/login`,
  RECOVER: `${IDP_BASE}/api/auth/recover`,
  RESET_PASSWORD: `${IDP_BASE}/api/auth/reset-password`,
  CHANGE_PASSWORD: `${IDP_BASE}/api/auth/change-password`,
  REFRESH: `${IDP_BASE}/api/auth/refresh`,
  LOGOUT: `${IDP_BASE}/api/auth/logout`,
  IMPERSONATE: `${IDP_BASE}/api/auth/impersonate`,
  STOP_IMPERSONATION: `${IDP_BASE}/api/auth/impersonation/stop`,
  SOCIAL_AUTHORIZE: `${IDP_BASE}/api/auth/social/authorize`,
  SOCIAL_CALLBACK: `${IDP_BASE}/api/auth/social/callback`,
  SOCIAL_LOGIN: `${IDP_BASE}/api/auth/social/callback`,
  OIDC_TOKEN: `${IDP_BASE}/api/oidc/token`,
  OIDC_LOGIN: `${IDP_BASE}/api/oidc/login`,
  OIDC_LOGIN_SELECT_ACCOUNT: `${IDP_BASE}/api/oidc/login/select-account`,
  TOKEN_EXCHANGE: `${IDP_BASE}/api/token/exchange`,
  GET_LOGIN_OPTIONS: `${IDP_BASE}/api/auth/login-options`,
  SIGNUP: `${IDP_BASE}/api/iam/users/signup`,
  ACTIVATE_ACCOUNT: `${IDP_BASE}/api/iam/users/activate`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${IDP_BASE}/api/oidc-clients`,
  GET_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`,   // append /{clientId} at call site
  SAVE_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`,
  DELETE_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`, // append /{clientId} at call site
} as const;

// ─── Auth configuration endpoints ───────────────────────────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${IDP_BASE}/api/iam/config`,
  UPDATE_CONFIG: `${IDP_BASE}/api/iam/config`,
} as const;

// ─── Auth client credentials endpoints ──────────────────────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `${IDP_BASE}/api/oidc-clients`,
  SAVE_CLIENT_CREDENTIAL: `${IDP_BASE}/api/oidc-clients`,
  DELETE_CLIENT_CREDENTIAL: `${IDP_BASE}/api/oidc-clients`,
} as const;
// ─── IAM Management endpoints ──────────────────────────────────────────────

export const IAM_ENDPOINTS = {
  GET_CONFIG: `${IDP_BASE}/api/iam/config`,
  UPDATE_CONFIG: `${IDP_BASE}/api/iam/config`,
  USERS: `${IDP_BASE}/api/iam/users`,
  ORGANIZATIONS: `${IDP_BASE}/api/iam/organizations`,
  ROLES: `${IDP_BASE}/api/iam/roles`,
  PERMISSIONS: `${IDP_BASE}/api/iam/permissions`,
} as const;

// ─── SSO provider management endpoints ──────────────────────────────────────
export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `${IDP_BASE}/auth/sso/credentials`,
  GET_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential`,
  SAVE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/save`,
  DELETE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/delete`,
  UPDATE_STATUS: `${IDP_BASE}/auth/sso/credential/status`,
} as const;

