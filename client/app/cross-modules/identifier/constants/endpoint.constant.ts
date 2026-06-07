import { BLOCKS_OS_BASE_URL } from "@/constants/endpoint.constant";

// ─── People endpoints ─────────────────────────────────────────────────────────

const PEOPLE_SUBPATH = "/People";

export const PEOPLE_ENDPOINTS = {
  CONFIRM_INVITATION: `/api${PEOPLE_SUBPATH}/ConfirmInvitation`,
  GETS: `/api${PEOPLE_SUBPATH}/Gets`,
  INVITE: `/api${PEOPLE_SUBPATH}/Invite`,
  RESEND_INVITATION: `/api${PEOPLE_SUBPATH}/ResendInvitation`,
  REMOVE_ACCESS: `/api${PEOPLE_SUBPATH}/RemoveAccess`,
  SIGNUP: `/api${PEOPLE_SUBPATH}/Signup`,
  TRANSFER_OWNERSHIP: `/api${PEOPLE_SUBPATH}/TransferOwnerShip`,
} as const;

// ─── Project endpoints ────────────────────────────────────────────────────────

const PROJECT_SUBPATH = "/Project";

export const PROJECT_ENDPOINTS = {
  GETS: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/Gets`,
  GET: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/Get`,
  CREATE: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/Create`,
  UPDATE: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/UpdateProject`,
  UPDATE_TENANT_GROUP: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/UpdateTenantGroup`,
  DISABLE: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/Disable`,

  GET_ASSET: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/GetAsset`,
  ADD_ASSET: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/AddAsset`,
  GET_LOGIN_OPTIONS: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/GetLoginOptions`,
  UPDATE_TOKEN_VALIDATION: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/UpdateTokenValidationParameters`,
  GET_TOKEN_VALIDATION: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/GetTokenValidationParameters`,
  ADD_JWT_CLAIM: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/AddJwtClaim`,
  GET_JWT_CLAIMS: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/GetThirdPartyJWTClaims`,
  SAVE_JWT_CLAIMS: `${BLOCKS_OS_BASE_URL}/api${PROJECT_SUBPATH}/SaveThirdPartyJWTClaims`,
} as const;

// ─── Domain endpoints ─────────────────────────────────────────────────────────

const DOMAIN_SUBPATH = "/Domain";

export const DOMAIN_ENDPOINTS = {
  CONFIGURE: `/api${DOMAIN_SUBPATH}/Configure`,
} as const;

// ─── Migration endpoints ──────────────────────────────────────────────────────

const MIGRATION_SUBPATH = "/Migration";

export const MIGRATION_ENDPOINTS = {
  MIGRATE: `/api${MIGRATION_SUBPATH}/Migrate`,
  VERIFY: `/api${MIGRATION_SUBPATH}/Verify`,
  GET_STATUS: `/api${MIGRATION_SUBPATH}/GetMigrationStatus`,
} as const;

// ─── Subscription endpoints ───────────────────────────────────────────────────

const SUBSCRIPTION_SUBPATH = "/Subscription";

export const SUBSCRIPTION_ENDPOINTS = {
  GETS: `/api${SUBSCRIPTION_SUBPATH}/Gets`,
} as const;

// ─── Service Registry endpoints ───────────────────────────────────────────────

const SERVICE_SUBPATH = "/Service";

export const SERVICE_REGISTRY_ENDPOINTS = {
  REGISTER: `/api${SERVICE_SUBPATH}/Register`,
  GET_ALL: `/api${SERVICE_SUBPATH}/GetAll`,
} as const;

// ─── Cloud Build endpoints ────────────────────────────────────────────────────

const DEPLOYMENT_SUBPATH = "/deployment";

export const CLOUD_BUILD_ENDPOINTS = {
  REPOS_LIST: `/api${DEPLOYMENT_SUBPATH}/GetReposList`,
  REPO_UPDATE: `/api${DEPLOYMENT_SUBPATH}/repo-update`,
} as const;
