import { http } from "@/lib/http-client";
import {
  ICreateOrUpdateOrganizationPayload,
  ICreateOrUpdateOrganizationResponse,
  IGetOrganizationByIdParams,
  IGetOrganizationByIdResponse,
  IGetOrganizationsParams,
  IGetOrganizationsResponse,
} from "@blocks-idp/iam/models/organization";
import {
  IOrganizationConfigPayload,
  IOrganizationConfigResponse,
  IOrganizationConfigSaveResponse,
} from "@blocks-idp/iam/models/organization-config.model";
import { ORGANIZATION_ENDPOINTS } from "../constants/endpoint.constant";

export class OrganizationService {
  getOrganizations(params: IGetOrganizationsParams): Promise<IGetOrganizationsResponse> {
    let url = `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATIONS}?page=${params.page}&pageSize=${params.pageSize}`;
    if (params.searchText) url += `&searchText=${params.searchText}`;
    return http.get(url);
  }

  getOrganizationById(params: IGetOrganizationByIdParams): Promise<IGetOrganizationByIdResponse> {
    return http.get(
      `${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION}?ProjectKey=${params.projectKey}&ItemId=${params.itemId}`,
    );
  }

  saveOrganization = (
    payload: ICreateOrUpdateOrganizationPayload,
  ): Promise<ICreateOrUpdateOrganizationResponse> => {
    return http.post(ORGANIZATION_ENDPOINTS.CREATE_ORGANIZATION, payload);
  };

  getOrganizationConfig(): Promise<IOrganizationConfigResponse | null> {
    return http.get(`${ORGANIZATION_ENDPOINTS.GET_ORGANIZATION_CONFIG}`);
  }

  saveOrganizationConfig = (
    payload: IOrganizationConfigPayload,
  ): Promise<IOrganizationConfigSaveResponse> => {
    return http.post(ORGANIZATION_ENDPOINTS.SAVE_ORGANIZATION_CONFIG, payload);
  };
}

export const organizationService = new OrganizationService();
