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
    query.set("page", String(params.page));
    query.set("pageSize", String(params.pageSize));
    if (params.searchText) query.set("filter.name", params.searchText);
    if (params.sort) {
      query.set("sort.property", params.sort.property);
      query.set("sort.isDescending", String(params.sort.isDescending));
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

  getOrganizationConfig(): Promise<IOrganizationConfigResponse | null> {
    return serviceInstances.idpService.get(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}`);
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
