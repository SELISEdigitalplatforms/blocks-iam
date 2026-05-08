// Inline base path (empty — HttpClient prepends BLOCKS_API_BASE_URL at runtime)
const IDP_BASE = "";

// ─── Auth endpoints (backend: /api/auth/*) ──────────────────────────────────

export const AUTH_ENDPOINTS = {
  LOGIN: `${IDP_BASE}/auth/login`,
  RECOVER: `${IDP_BASE}/auth/recover`,
  RESET_PASSWORD: `${IDP_BASE}/auth/reset-password`,
  CHANGE_PASSWORD: `${IDP_BASE}/auth/change-password`,
  REFRESH: `${IDP_BASE}/auth/refresh`,
  LOGOUT: `${IDP_BASE}/auth/logout`,
  IMPERSONATE: `${IDP_BASE}/auth/impersonate`,
  STOP_IMPERSONATION: `${IDP_BASE}/auth/impersonation/stop`,
  SOCIAL_AUTHORIZE: `${IDP_BASE}/auth/social/authorize`,
  SOCIAL_CALLBACK: `${IDP_BASE}/auth/social/callback`,
  SOCIAL_LOGIN: `${IDP_BASE}/auth/social/callback`,
  OIDC_TOKEN: `${IDP_BASE}/oidc/token`,
  OIDC_LOGIN: `${IDP_BASE}/oidc/login`,
  OIDC_LOGIN_SELECT_ACCOUNT: `${IDP_BASE}/oidc/login/select-account`,
  TOKEN_EXCHANGE: `${IDP_BASE}/token/exchange`,
  GET_LOGIN_OPTIONS: `${IDP_BASE}/auth/login-options`,
  SIGNUP: `${IDP_BASE}/iam/users/signup`,
  ACTIVATE_ACCOUNT: `${IDP_BASE}/iam/users/activate`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${IDP_BASE}/oidc-clients`,
  GET_OIDC_CLIENT: `${IDP_BASE}/oidc-clients`,   // append /{clientId} at call site
  SAVE_OIDC_CLIENT: `${IDP_BASE}/oidc-clients`,
  DELETE_OIDC_CLIENT: `${IDP_BASE}/oidc-clients`, // append /{clientId} at call site
} as const;

// ─── Auth configuration endpoints ───────────────────────────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${IDP_BASE}/iam/config`,
  UPDATE_CONFIG: `${IDP_BASE}/iam/config`,
} as const;

// ─── Auth client credentials endpoints ──────────────────────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `${IDP_BASE}/oidc-clients`,
  SAVE_CLIENT_CREDENTIAL: `${IDP_BASE}/oidc-clients`,
  DELETE_CLIENT_CREDENTIAL: `${IDP_BASE}/oidc-clients`,
} as const;
// ─── IAM Management endpoints ──────────────────────────────────────────────

export const IAM_ENDPOINTS = {
  GET_CONFIG: `${IDP_BASE}/iam/config`,
  UPDATE_CONFIG: `${IDP_BASE}/iam/config`,
  USERS: `${IDP_BASE}/iam/users`,
  ORGANIZATIONS: `${IDP_BASE}/iam/organizations`,
  ROLES: `${IDP_BASE}/iam/roles`,
  PERMISSIONS: `${IDP_BASE}/iam/permissions`,
} as const;

// ─── OIDC flow endpoints ─────────────────────────────────────────────────────
export const OIDC_FLOW_ENDPOINTS = {
  USER_ACKNOWLEDGEMENT: `${IDP_BASE}/oidc/acknowledge`,
} as const;

// ─── SSO provider management endpoints ──────────────────────────────────────
export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `${IDP_BASE}/auth/sso/credentials`,
  GET_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential`,
  SAVE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/save`,
  DELETE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/delete`,
  UPDATE_STATUS: `${IDP_BASE}/auth/sso/credential/status`,
} as const;

