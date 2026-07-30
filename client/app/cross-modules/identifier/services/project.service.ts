import { serviceInstances } from "@/lib/http-client";
import {
  ICreateProjectPayload,
  IDisableProjectPayload,
  IDisableProjectResponse,
  IEnvRepository,
  IGetProjectLoginOptionResponse,
  IGetProjectPayload,
  IGetProjectResponse,
  IGetPublicCertificateResponse,
  IGetSubscriptionUsageResponse,
  IMigrationInitiateResponse,
  IMigrationRequest,
  IMigrationStatusResponse,
  IMigrationVerificationResponse,
  IProjectGroup,
  IResource,
  ISavePublicCertificatePayload,
  IUpdateProjectPayload,
  IUpdateTenantGroupPayload,
  IUpdateProjectResponse,
  IValidateCNameProjectPayload,
  IValidateCNameProjectResponse,
  IVerifyMigrationRequest,
} from "@blocks-identifier/models/project.model";
import {
  GetJwtClaimPayload,
  JwtClaimPayload,
  JwtClaimResponse,
} from "@blocks-idp/authentication/models/jwt.claim.model";
import {
  PROJECT_ENDPOINTS,
  DOMAIN_ENDPOINTS,
  MIGRATION_ENDPOINTS,
  SUBSCRIPTION_ENDPOINTS,
  CLOUD_BUILD_ENDPOINTS,
} from "@blocks-identifier/constants/endpoint.constant";

export class ProjectService {
  private readonly logicClient = serviceInstances.logicService;
  getProjects(page: number, pageSize: number, tenantGroupId: string): Promise<IProjectGroup[]> {
    const url = `${PROJECT_ENDPOINTS.GETS}?page=${page}&pageSize=${pageSize}&tenantGroupId=${tenantGroupId}`;
    return serviceInstances.idpService.get(url, undefined, { absoluteUrl: true });
  }

  getAssets(tenantGroupId: string): Promise<{
    assets: {
      resources: IResource[];
      tenantGroupId: string;
      createdDate: string;
      itemId: string;
    };
    totalCount: number;
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    const url = `${PROJECT_ENDPOINTS.GET_ASSET}?TenantGroupId=${tenantGroupId}`;
    return serviceInstances.idpService.get(url, undefined, { absoluteUrl: true });
  }

  addAssets(payload: { tenantGroupId: string; resource: IResource }): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.ADD_ASSET, payload, undefined, { absoluteUrl: true });
  }

  getEnvRepositories(projectKey: string): Promise<{
    data: IEnvRepository[];
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    const url = `${CLOUD_BUILD_ENDPOINTS.REPOS_LIST}?projectkey=${projectKey}`;
    return this.logicClient.get(url);
  }

  repoUpdate(payload: {
    projectKey: string;
    projectEnv: string;
    repoWithDomains: {
      repoId: string;
      repoUrl: string;
      customDeploymentDomain: string;
    }[];
  }): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return serviceInstances.idpService.post(CLOUD_BUILD_ENDPOINTS.REPO_UPDATE, payload);
  }

  getProject(payload: IGetProjectPayload): Promise<IGetProjectResponse> {
    const url = `${PROJECT_ENDPOINTS.GET}?projectId=${payload.projectId}`;
    return serviceInstances.idpService.get(url, undefined, { absoluteUrl: true });
  }

  createProject(payload: ICreateProjectPayload): Promise<{
    isSuccess: boolean;
    errors: Record<string, string | string[]>;
    tenantGroupId: string;
  }> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.CREATE, payload, undefined, { absoluteUrl: true });
  }

  validateCNameProject(
    payload: IValidateCNameProjectPayload,
  ): Promise<IValidateCNameProjectResponse> {
    return serviceInstances.idpService.post(DOMAIN_ENDPOINTS.CONFIGURE, payload);
  }

  updateProject(payload: IUpdateProjectPayload): Promise<IUpdateProjectResponse> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.UPDATE, payload, undefined, { absoluteUrl: true });
  }

  updateTenantGroup(payload: IUpdateTenantGroupPayload): Promise<IUpdateProjectResponse> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.UPDATE_TENANT_GROUP, payload, undefined, { absoluteUrl: true });
  }

  disableProject(payload: IDisableProjectPayload): Promise<IDisableProjectResponse> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.DISABLE, payload, undefined, { absoluteUrl: true });
  }

  getProjectLoginOption(): Promise<IGetProjectLoginOptionResponse> {
    return serviceInstances.idpService.get(PROJECT_ENDPOINTS.GET_LOGIN_OPTIONS, undefined, { absoluteUrl: true });
  }

  // Data Migration Methods
  initiateMigration(payload: IMigrationRequest): Promise<IMigrationInitiateResponse> {
    return serviceInstances.idpService.post(MIGRATION_ENDPOINTS.MIGRATE, payload);
  }

  verifyMigration(payload: IVerifyMigrationRequest): Promise<IMigrationVerificationResponse> {
    return serviceInstances.idpService.post(MIGRATION_ENDPOINTS.VERIFY, payload);
  }

  getMigrationStatus(_tenantGroupId: string): Promise<IMigrationStatusResponse> {
    return Promise.resolve([]);
  }

  savePublicCertificate(payload: ISavePublicCertificatePayload): Promise<IUpdateProjectResponse> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.UPDATE_TOKEN_VALIDATION, payload, undefined, { absoluteUrl: true });
  }

  getPublicCertificateInformation(
    projectKey: string,
  ): Promise<IGetPublicCertificateResponse | null> {
    const url = `${PROJECT_ENDPOINTS.GET_TOKEN_VALIDATION}?ProjectKey=${projectKey}`;
    return serviceInstances.idpService.get<IGetPublicCertificateResponse | null>(url, undefined, { absoluteUrl: true });
  }

  async validateJwksUrl(url: string): Promise<{
    isValid: boolean;
    error?: string;
    data?: unknown;
  }> {
    try {
      const response = await fetch(url, {
        method: "GET",
        headers: {
          "Content-Type": "application/json",
        },
      });

      if (!response.ok) {
        // invalid
        // HTTP error
        return {
          isValid: false,
          error: `Invalid, provide a valid jwks URL`,
        };
      }

      const contentType = response.headers.get("content-type");
      if (!contentType?.includes("application/json")) {
        // invalid
        // Response is not JSON
        return {
          isValid: false,
          error: "Invalid, provide a valid jwks URL",
        };
      }

      const json = await response.json();

      // Structure validation
      if (!json.keys || !Array.isArray(json.keys) || json.keys.length === 0) {
        // invalid
        // Missing or invalid 'keys' array in JWKS
        return {
          isValid: false,
          error: "Invalid, provide a valid jwks URL",
        };
      }

      return { isValid: true, data: json };
    } catch (error) {
      console.error("JWKS URL Validation Error:", error);
      return {
        isValid: false,
        error: "Invalid, provide a valid jwks URL",
      };
    }
  }

  getJwtClaim(payload: GetJwtClaimPayload): Promise<JwtClaimResponse> {
    const url = `${PROJECT_ENDPOINTS.GET_JWT_CLAIMS}?ProjectKey=${payload.projectKey}&ItemId=${payload.itemId}`;
    return serviceInstances.idpService.get(url, undefined, { absoluteUrl: true });
  }

  addJwtClaim(payload: JwtClaimPayload): Promise<{
    errors: unknown | null;
    isSuccess: boolean;
  }> {
    return serviceInstances.idpService.post(PROJECT_ENDPOINTS.SAVE_JWT_CLAIMS, payload, undefined, { absoluteUrl: true });
  }

  getSubscriptionUsage(projectKey: string): Promise<IGetSubscriptionUsageResponse> {
    return serviceInstances.idpService.get(`${SUBSCRIPTION_ENDPOINTS.GETS}?projectKey=${projectKey}`);
  }
}

export const projectService = new ProjectService();
