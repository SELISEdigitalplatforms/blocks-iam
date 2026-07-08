export interface IDomain {
  domain: string;
  cookieDomain?: string;
  isDomainVerified?: boolean;
  isDefault?: boolean;
}

export interface IProject {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  name: string;
  applicationDomain: string;
  customDomain: string;
  isProduction: true;
  tenantId: string;
  isCookieEnable: boolean;
  isDomainVerified: boolean;
  cookieDomain: string;
  isDisabled: boolean;
  environment: string;
  tenantGroupId: string;
  tenantSlug: string;
  applications?: IDomain[];
}

export interface IGetProjectPayload {
  tenantId?: string;
  projectId?: string;
}

export interface IGetProjectResponse {
  data: IProject;
  errors: unknown | null;
}

export interface IProjectGroup {
  tenantGroupId: string;
  projects: IProject[];
  nonSharedProject: IProject[];
  isShared: boolean;
}

export interface IEnvRepository {
  itemId: string;
  repoName: string;
  repoUrl: string;
  defaultDeploymentUrl: string;
  customDeploymentUrl: string;
  lastDeploymentDate: string;
}

export interface IDisableProjectPayload {
  tenantId?: string;
  projectKey?: string;
  isDisabled?: boolean;
}

export interface IDisableProjectResponse {
  errors: string | null;
  isSuccess: boolean;
}

export interface IUpdateProjectResponse {
  errors: unknown | null;
  isSuccess: boolean;
}

export interface IUpdateTenantGroupPayload {
  tenantGroupId: string;
  name?: string;
  projectIds?: string[];
}