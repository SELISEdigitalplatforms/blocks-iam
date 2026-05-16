/**
 * Admin API Endpoints
 * All endpoints are under /api/iam/* prefix
 */

export const ADMIN_ENDPOINTS = {
  // User Management
  USER: {
    LIST: '/api/iam/users',
    CREATE: '/api/iam/users/create',
    UPDATE: '/api/iam/users/update',
    DEACTIVATE: '/api/iam/users/deactivate',
    GET: '/api/iam/user',
    GET_TIMELINES: '/api/iam/user/timelines',
    CHECK_EMAIL: '/api/iam/email/available',
  },

  // Organization Management
  ORGANIZATION: {
    LIST: '/api/iam/organizations',
    CREATE: '/api/iam/organizations',
    GET: '/api/iam/organization',
    UPDATE: '/api/iam/organizations',
    GET_CONFIG: '/api/iam/organization/config',
    SAVE_CONFIG: '/api/iam/organization/config',
  },

  // Session Management
  SESSION: {
    LIST: '/api/iam/sessions',
    GET_HISTORY: '/api/iam/history',
  },

  // Activity/Audit
  ACTIVITY: {
    GET_TIMELINES: '/api/iam/user/timelines',
  },

  // OIDC Client Management
  OIDC_CLIENT: {
    LIST: '/api/oidc-clients',
    CREATE: '/api/oidc-clients',
    GET: '/api/oidc-clients',
    UPDATE: '/api/oidc-clients',
    DELETE: '/api/oidc-clients',
  },

  // Account/Config
  ACCOUNT: {
    GET: '/api/iam/account',
    UPDATE: '/api/iam/account/update',
    GET_ACCOUNTS: '/api/iam/accounts',
    GET_ROLES: '/api/iam/account/roles',
    GET_PERMISSIONS: '/api/iam/account/permissions',
  },

  // Configuration
  CONFIG: {
    GET_SIGNUP_SETTINGS: '/api/iam/signup-settings',
    SAVE_SIGNUP_SETTINGS: '/api/iam/signup-settings',
    GET_IAM_CONFIG: '/api/iam/config',
    SAVE_IAM_CONFIG: '/api/iam/config',
  },

  // Role & Permission Management (optional)
  ROLE: {
    LIST: '/api/iam/roles',
    CREATE: '/api/iam/roles/create',
    UPDATE: '/api/iam/roles/update',
    GET: '/api/iam/role',
    ASSIGN: '/api/iam/roles/assign',
  },

  PERMISSION: {
    LIST: '/api/iam/permissions',
    CREATE: '/api/iam/permissions/create',
    UPDATE: '/api/iam/permissions/update',
    GET: '/api/iam/permission',
    BY_SEVERITY: '/api/iam/permissions/by-severity',
  },
};
