export interface IOrganization {
  itemId: string;
  name: string;
  description: string | null;
  parentOrganizationId: string | null;
  shortCode: string | null;
  isDisabled: boolean;
  defaultRoleForMembers: string[];
  defaultPermissionsForMembers: string[];
  email: string | null;
  phoneNumber: string | null;
  websiteUrl: string | null;
  addresses: unknown[];
  theme: string | null;
  logoUrl: string | null;
  logoId: string | null;
  industry: string | null;
  timeZone: string;
  currency: string | null;
  dateFormat: string;
  timeFormat: string;
  locale: string;
  attributes: Record<string, unknown>;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string | null;
  lastUpdatedBy: string;
  organizationId: string;
  organizationIds?: string[];
  tags: string[];
}

export interface IOrganizationFilter {
  projectKey?: string;
  page: number;
  pageSize: number;
  search?: string;
  sort?: {
    property: string;
    isDescending: boolean;
  };
}

export interface IGetOrganizationsParams {
  page: number;
  pageSize: number;
  search?: string;
  isDisabled?: boolean;
  parentOrganizationId?: string;
  sort?: {
    property: string;
    isDescending: boolean;
  };
}

export interface IGetOrganizationsResponse {
  organizations: IOrganization[];
  errors: unknown;
  isSuccess: boolean;
  totalCount: number;
}

export interface IGetOrganizationByIdParams {
  projectKey: string;
  itemId: string;
}

export interface IGetOrganizationByIdResponse {
  organization: IOrganization;
  errors: unknown;
  isSuccess: boolean;
}

export interface ICreateOrUpdateOrganizationPayload {
  name: string;
  createdFrom?: number;
}

export interface IUpdateOrganizationPayload {
  itemId: string;
  name: string;
  isEnable: boolean;
}

export interface ICreateOrUpdateOrganizationResponse {
  errors: unknown;
  isSuccess: boolean;
}

export interface IMyOrganization {
  itemId: string;
  name: string;
  createdDate: string;
}

export interface IGetMyOrganizationsResponse {
  organizations: IMyOrganization[];
  errors: unknown;
  isSuccess: boolean;
}
