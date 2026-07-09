import { GRANT_TYPES, SSO_PROVIDERS } from "@blocks-idp/authentication/constants";

export type {
  IDisableProjectPayload,
  IDisableProjectResponse,
  IDomain,
  IEnvRepository,
  IGetProjectPayload,
  IGetProjectResponse,
  IProject,
  IProjectGroup,
  IUpdateProjectResponse,
  IUpdateTenantGroupPayload,
} from "@/models/project.model";

export interface IResource {
  name: string;
  link: string;
  resourceId: string;
}
export interface ICreateProjectPayload {
  name: string;
  isAcceptBlocksTerms: boolean;
  isUseBlocksExclusively: boolean;
  isProduction: boolean;
  resources: IResource[];
  applicationContexts: {
    environment: string;
    domain: string;
    cookieDomain: string;
  }[];
  tenantGroupId?: string;
}
export interface IGetProjectAuthConfig {
  accountLockDurationInMinutes: number;
  certificateValidForNumberOfDays: number;
  getNumberOfWrongAttemptsToLockTheAccount: number;
  publicCertificatePath: string;
  certificateIssueDate: string;
  refreshTokenValidForNumberMinutes: number;
  allowedGrantTypes: string[];
}
export interface IGetProjectAuthConfigPayload {
  projectId: string;
}
export interface IGetProjectAuthConfigResponse extends IGetProjectAuthConfig {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISaveProjectAuthConfigPayload {
  refreshTokenValidForNumberMinutes: number;
  getNumberOfWrongAttemptsToLockTheAccount: number;
  accountLockDurationInMinutes: number;
  projectId: string;
  allowedGrantTypes: string[];
}
export interface ISaveProjectAuthConfigResponse {
  errors: unknown | null;
  isSuccess: boolean;
}
export interface ISavePublicCertificatePayload {
  projectKey: string;
  publicCertificatePassword: string;
  issuer: string;
  audiences: string[];
  publicCertificatePath: string;
  jwksUrl: string;
  providerName: string;
  cookieKey?: string;
}

export interface IValidateCNameProjectPayload {
  projectKey: string;
  cookieDomain: string;
}
export interface IValidateCNameProjectResponse {
  errors: unknown | null;
  isSuccess: boolean;
  isStatusChanged: boolean;
}

export interface IUpdateProjectPayload {
  name: string;
  applicationDomain: string;
  isCookieEnable?: boolean;
  cookieDomain?: string;
  useCustomDomain: boolean;
  customDomain: string;
  projectKey: string;
}
type SSO_INFO = {
  provider: SSO_PROVIDERS;
  audience: string;
};

export type LoginOption = {
  allowedGrantTypes: GRANT_TYPES[];
  ssoInfo: SSO_INFO[];
};
export type IGetProjectLoginOptionResponse = LoginOption;

// Data Migration interfaces
export interface IMigrationServiceDetails {
  shouldOverWriteExistingData: boolean;
  serviceName: number;
}

export interface IMigrationRequest {
  projectKey: string;
  targetedProjectKey: string;
  tenantGroupId: string;
  services: IMigrationServiceDetails[];
}

export interface IMigrationInitiateResponse {
  verificationId: string;
  isSuccess: boolean;
}

export interface IVerifyMigrationRequest {
  verificationId: string;
  verificationCode: string;
}

export interface IMigrationVerificationResponse {
  isValid: boolean;
  isSuccess: boolean;
  errors: unknown | null;
}

export type IMigrationStatusResponse = Array<{
  targetedProjectKey: string;
}>;

export interface IGetPublicCertificateResponse {
  issuer: string;
  audiences: string[];
  publicCertificatePath: string;
  jwksUrl: string;
  cookieKey: string | null;
  isConfigured: boolean;
  providerName: string | null;
}

export interface ISubscription {
  resource: string;
  resourceType: string | null;
  limit: number;
  usage: number;
  lifetime: string;
  isActive: boolean;
  enableAutoRenew: boolean;
  tenantId: string;
  type: string;
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
}

export interface IGetSubscriptionUsageResponse {
  subscriptions: ISubscription[];
  errors: unknown | null;
  isSuccess: boolean;
}
