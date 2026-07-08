export type {
  IDomain,
  IProject,
  IProjectGroup,
  IGetProjectResponse,
  IEnvRepository,
} from "@seliseblocks/blocks-kit/models";

export interface IGetProjectPayload {
  tenantId?: string;
  projectId?: string;
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