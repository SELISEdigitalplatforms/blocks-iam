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







// import { API_BASES } from "@/constants/endpoint.constant";
// const IDP_BASE = "";

// // ─── Subpaths ─────────────────────────────────────────────────────────────────

// const AUTH_SUBPATH = "/Authentication";

// // ─── Auth endpoints (auth.service / oauth.service) ───────────────────────────

// // export const AUTH_ENDPOINTS = {
// //   TOKEN: `${API_BASES.IDP}${AUTH_SUBPATH}/Token`,
// //   LOGOUT: `${API_BASES.IDP}${AUTH_SUBPATH}/Logout`,
// //   GET_SOCIAL_LOGIN_ENDPOINT: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSocialLogInEndPoint`,
// //   GET_LOGIN_OPTIONS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetLoginOptions`,
// // } as const;

// export const AUTH_ENDPOINTS = {
//   LOGIN: `${IDP_BASE}/api/auth/login`,
//   RECOVER: `${IDP_BASE}/api/auth/recover`,
//   RESET_PASSWORD: `${IDP_BASE}/api/auth/reset-password`,
//   CHANGE_PASSWORD: `${IDP_BASE}/api/auth/change-password`,
//   REFRESH: `${IDP_BASE}/api/auth/refresh`,
//   LOGOUT: `${IDP_BASE}/api/auth/logout`,
//   IMPERSONATE: `${IDP_BASE}/api/auth/impersonate`,
//   STOP_IMPERSONATION: `${IDP_BASE}/api/auth/impersonation/stop`,
//   SOCIAL_AUTHORIZE: `${IDP_BASE}/api/auth/social/authorize`,
//   SOCIAL_CALLBACK: `${IDP_BASE}/api/auth/social/callback`,
//   SOCIAL_LOGIN: `${IDP_BASE}/api/auth/social/callback`,
//   OIDC_TOKEN: `${IDP_BASE}/api/oidc/token`,
//   OIDC_LOGIN: `${IDP_BASE}/api/oidc/login`,
//   OIDC_LOGIN_SELECT_ACCOUNT: `${IDP_BASE}/api/oidc/login/select-account`,
//   TOKEN_EXCHANGE: `${IDP_BASE}/api/token/exchange`,
//   GET_LOGIN_OPTIONS: `${IDP_BASE}/api/auth/login-options`,
//   SIGNUP: `${IDP_BASE}/api/iam/users/signup`,
//   ACTIVATE_ACCOUNT: `${IDP_BASE}/api/iam/users/activate`,
// } as const;

// // ─── Client credential endpoints (auth-clients.service) ─────────────────────

// // export const AUTH_CLIENT_ENDPOINTS = {
// //   GET_CLIENT_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetClientCredentials`,
// //   SAVE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveClientCredential`,
// //   DELETE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteClientCredential`,
// // } as const;

// export const AUTH_CLIENT_ENDPOINTS = {
//   GET_CLIENT_CREDENTIALS: `${IDP_BASE}/api/oidc-clients`,
//   SAVE_CLIENT_CREDENTIAL: `${IDP_BASE}/api/oidc-clients`,
//   DELETE_CLIENT_CREDENTIAL: `${IDP_BASE}/api/oidc-clients`,
// } as const;

// // ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

// // export const AUTH_OIDC_ENDPOINTS = {
// //   GET_OIDC_CLIENTS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetOIDCClients`,
// //   GET_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/GetOIDCClient`,
// //   SAVE_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveOIDCClient`,
// //   DELETE_OIDC_CLIENT: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteOIDCClient`,
// // } as const;

// export const AUTH_OIDC_ENDPOINTS = {
//   GET_OIDC_CLIENTS: `${IDP_BASE}/api/oidc-clients`,
//   GET_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`,   // append /{clientId} at call site
//   SAVE_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`,
//   DELETE_OIDC_CLIENT: `${IDP_BASE}/api/oidc-clients`, // append /{clientId} at call site
// } as const;


// // ─── Auth configuration endpoints (auth-config.service) ─────────────────────

// // export const AUTH_CONFIG_ENDPOINTS = {
// //   GET_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Get`,
// //   UPDATE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Update`,
// // } as const;

// export const AUTH_CONFIG_ENDPOINTS = {
//   GET_CONFIG: `${IDP_BASE}/api/iam/config`,
//   UPDATE_CONFIG: `${IDP_BASE}/api/iam/config`,
// } as const;

// // ─── SSO endpoints (social.service) ─────────────────────────────────────────

// // export const SSO_ENDPOINTS = {
// //   GET_SSO_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredentials`,
// //   GET_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredential`,
// //   SAVE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveSsoCredential`,
// //   DELETE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteSsoCredential`,
// //   UPDATE_STATUS: `${API_BASES.IDP}${AUTH_SUBPATH}/UpdateStatus`,
// // } as const;

// // ─── OIDC flow endpoints (oidc-auth-flow.service) ───────────────────────────

// export const OIDC_FLOW_ENDPOINTS = {
//   USER_ACKNOWLEDGEMENT: `${API_BASES.IDP}${AUTH_SUBPATH}/UserAcknowledgement`,
// } as const;

// // ─── Legacy re-export (backward compat for oauth.service) ───────────────────

// // export const IDP_ENDPOINTS = {
// //   AUTHENTICATION: {
// //     GET_SOCIAL_LOGIN_ENDPOINT: AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT,
// //     TOKEN: AUTH_ENDPOINTS.TOKEN,
// //   },
// // };


// export const IAM_ENDPOINTS = {
//   GET_CONFIG: `${IDP_BASE}/api/iam/config`,
//   UPDATE_CONFIG: `${IDP_BASE}/api/iam/config`,
//   USERS: `${IDP_BASE}/api/iam/users`,
//   ORGANIZATIONS: `${IDP_BASE}/api/iam/organizations`,
//   ROLES: `${IDP_BASE}/api/iam/roles`,
//   PERMISSIONS: `${IDP_BASE}/api/iam/permissions`,
// } as const;

// export const SSO_ENDPOINTS = {
//   GET_SSO_CREDENTIALS: `${IDP_BASE}/auth/sso/credentials`,
//   GET_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential`,
//   SAVE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/save`,
//   DELETE_SSO_CREDENTIAL: `${IDP_BASE}/auth/sso/credential/delete`,
//   UPDATE_STATUS: `${IDP_BASE}/auth/sso/credential/status`,
// } as const;