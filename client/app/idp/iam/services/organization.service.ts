import { serviceInstances } from "@/lib/http-client";
import {
  ICreateOrUpdateOrganizationPayload,
  ICreateOrUpdateOrganizationResponse,
  IGetMyOrganizationsResponse,
  IGetOrganizationByIdParams,
  IGetOrganizationByIdResponse,
  IGetOrganizationsParams,
  IGetOrganizationsResponse,
  IUpdateOrganizationPayload,
} from "@blocks-idp/iam/models/organization";
import {
  IOrganizationConfigPayload,
  IOrganizationConfigResponse,
  IOrganizationConfigSaveResponse,
} from "@blocks-idp/iam/models/organization-config.model";
import { ORGANIZATION_ENDPOINTS } from "../constants/endpoint.constant";

export class OrganizationService {
  getOrganizations(params: IGetOrganizationsParams): Promise<IGetOrganizationsResponse> {
    const query = new URLSearchParams();
    query.set("Page", String(params.page));
    query.set("PageSize", String(params.pageSize));
    if (params.search) query.set("Filter.Search", params.search);
    if (params.isDisabled !== undefined) query.set("Filter.IsDisabled", String(params.isDisabled));
    if (params.parentOrganizationId)
      query.set("Filter.ParentOrganizationId", params.parentOrganizationId);
    if (params.sort) {
      query.set("Sort.Property", params.sort.property);
      query.set("Sort.IsDescending", String(params.sort.isDescending));
    }
    return serviceInstances.idpService.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?${query.toString()}`,
    );
  }

  getOrganizationById(params: IGetOrganizationByIdParams): Promise<IGetOrganizationByIdResponse> {
    return serviceInstances.idpService.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}/${params.itemId}`,
    );
  }

  saveOrganization = (
    payload: ICreateOrUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    return serviceInstances.idpService.post(ORGANIZATION_ENDPOINTS.CREATE_ORGANIZATION, payload);
  };

  updateOrganization = (
    payload: IUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    return serviceInstances.idpService.post(`${ORGANIZATION_ENDPOINTS.UPDATE_ORGANIZATION}/${payload.itemId}`, {
      name: payload.name,
      isEnable: payload.isEnable,
    });
  };

  // `tenantId` is only supplied by the anonymous signup page, which has no
  // session to resolve the tenant from and must read the config of the tenant
  // named in the OIDC request. Authenticated callers omit it and fall back to
  // the http client's default Blocks key.
  getOrganizationConfig(tenantId?: string): Promise<IOrganizationConfigResponse | null> {
    const headers: Record<string, string> = {};
    if (tenantId) {
      headers["X-Blocks-Key"] = tenantId;
    }
    const url = tenantId
      ? `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}?tenantId=${encodeURIComponent(tenantId)}`
      : ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG;
    return serviceInstances.idpService.get(
      url,
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    );
  }

  getMyOrganizations(): Promise<IGetMyOrganizationsResponse> {
    return serviceInstances.idpService.get(ORGANIZATION_ENDPOINTS.GET_MY_ORGANIZATIONS);
  }

  saveOrganizationConfig = (
    payload: IOrganizationConfigPayload,
  ): Promise<IOrganizationConfigSaveResponse> => {
    return serviceInstances.idpService.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG, payload);
  };
}

export const organizationService = new OrganizationService();
