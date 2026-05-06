import { API_BASES } from "@/constants/endpoint.constant";

// ─── Subpaths ─────────────────────────────────────────────────────────────────

const AUTH_SUBPATH = "/auth";
const LEGACY_AUTH_SUBPATH = "/Authentication";
const OIDC_SUBPATH = "/oidc";

// ─── Auth endpoints (auth.service / oauth.service) ───────────────────────────

export const AUTH_ENDPOINTS = {
  TOKEN: `${API_BASES.IDP}${OIDC_SUBPATH}/token`,
  LEGACY_TOKEN: `${API_BASES.IDP}${LEGACY_AUTH_SUBPATH}/Token`,
  OIDC_TOKEN: `${API_BASES.IDP}${OIDC_SUBPATH}/token`,
  OIDC_LOGIN: `${API_BASES.IDP}${OIDC_SUBPATH}/login`,
  OIDC_LOGIN_SELECT_ACCOUNT: `${API_BASES.IDP}${OIDC_SUBPATH}/login/select-account`,
  LOGIN: `${API_BASES.IDP}${AUTH_SUBPATH}/login`,
  SOCIAL_LOGIN: `${API_BASES.IDP}${AUTH_SUBPATH}/social-login`,
  RECOVER: `${API_BASES.IDP}${AUTH_SUBPATH}/recover`,
  RESET_PASSWORD: `${API_BASES.IDP}${AUTH_SUBPATH}/reset-password`,
  CHANGE_PASSWORD: `${API_BASES.IDP}${AUTH_SUBPATH}/change-password`,
  REFRESH: `${API_BASES.IDP}${AUTH_SUBPATH}/refresh`,
  LOGOUT: `${API_BASES.IDP}${AUTH_SUBPATH}/logout`,
  IMPERSONATE: `${API_BASES.IDP}${AUTH_SUBPATH}/impersonate`,
  STOP_IMPERSONATION: `${API_BASES.IDP}${AUTH_SUBPATH}/stop-impersonation`,
  GET_SOCIAL_LOGIN_ENDPOINT: `${API_BASES.IDP}${LEGACY_AUTH_SUBPATH}/GetSocialLogInEndPoint`,
  GET_LOGIN_OPTIONS: `${API_BASES.IDP}${AUTH_SUBPATH}/login-options`,
} as const;

// ─── Client credential endpoints (auth-clients.service) ─────────────────────

export const AUTH_CLIENT_ENDPOINTS = {
  GET_CLIENT_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetClientCredentials`,
  SAVE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveClientCredential`,
  DELETE_CLIENT_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteClientCredential`,
} as const;

// ─── OIDC client endpoints (auth-clients-oidc.service) ──────────────────────

const OIDC_CLIENTS_SUBPATH = "/oidc-clients";

export const AUTH_OIDC_ENDPOINTS = {
  GET_OIDC_CLIENTS: `${API_BASES.IDP}${OIDC_CLIENTS_SUBPATH}`,
  GET_OIDC_CLIENT: `${API_BASES.IDP}${OIDC_CLIENTS_SUBPATH}`,   // append /{clientId} at call site
  SAVE_OIDC_CLIENT: `${API_BASES.IDP}${OIDC_CLIENTS_SUBPATH}`,
  DELETE_OIDC_CLIENT: `${API_BASES.IDP}${OIDC_CLIENTS_SUBPATH}`, // append /{clientId} at call site
} as const;

// ─── Auth configuration endpoints (auth-config.service) ─────────────────────

export const AUTH_CONFIG_ENDPOINTS = {
  GET_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Get`,
  UPDATE_CONFIG: `${API_BASES.CLOUD_CONFIGURATION}${AUTH_SUBPATH}/Update`,
} as const;

// ─── SSO endpoints (social.service) ─────────────────────────────────────────

export const SSO_ENDPOINTS = {
  GET_SSO_CREDENTIALS: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredentials`,
  GET_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/GetSsoCredential`,
  SAVE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/SaveSsoCredential`,
  DELETE_SSO_CREDENTIAL: `${API_BASES.IDP}${AUTH_SUBPATH}/DeleteSsoCredential`,
  UPDATE_STATUS: `${API_BASES.IDP}${AUTH_SUBPATH}/UpdateStatus`,
} as const;

// ─── OIDC flow endpoints (oidc-auth-flow.service) ───────────────────────────

export const OIDC_FLOW_ENDPOINTS = {
  USER_ACKNOWLEDGEMENT: `${API_BASES.IDP}${AUTH_SUBPATH}/UserAcknowledgement`,
} as const;

// ─── Legacy re-export (backward compat for oauth.service) ───────────────────

export const IDP_ENDPOINTS = {
  AUTHENTICATION: {
    GET_SOCIAL_LOGIN_ENDPOINT: AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT,
    TOKEN: AUTH_ENDPOINTS.LEGACY_TOKEN,
  },
};
