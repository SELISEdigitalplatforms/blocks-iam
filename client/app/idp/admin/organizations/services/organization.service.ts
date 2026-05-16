import { http } from '@/lib/http-client';
import {
  Organization,
  CreateOrganizationRequest,
  UpdateOrganizationRequest,
  GetOrganizationsResponse,
  GetOrganizationResponse,
  OrganizationConfig,
  SaveOrganizationConfigRequest,
} from '@blocks-idp/shared/models/admin.models';
import { ADMIN_ENDPOINTS } from '@blocks-idp/admin/constants/endpoint.constant';

/**
 * Organization Management Service
 * Handles CRUD operations for organizations in IDP Admin
 */
export const organizationService = {
  /**
   * Get list of organizations
   */
  async getOrganizations(page?: number, pageSize?: number): Promise<GetOrganizationsResponse> {
    const params = new URLSearchParams();
    if (page) params.set('page', String(page));
    if (pageSize) params.set('page_size', String(pageSize));
    const qs = params.toString();
    return http.get(`${ADMIN_ENDPOINTS.ORGANIZATION.LIST}${qs ? `?${qs}` : ''}`);
  },

  /**
   * Get single organization by ID
   */
  async getOrganization(organizationId: string): Promise<GetOrganizationResponse> {
    return http.get(`${ADMIN_ENDPOINTS.ORGANIZATION.GET}?id=${organizationId}`);
  },

  /**
   * Create new organization
   */
  async createOrganization(data: CreateOrganizationRequest): Promise<{ success: boolean; organization?: Organization }> {
    return http.post(ADMIN_ENDPOINTS.ORGANIZATION.CREATE, data);
  },

  /**
   * Update organization information
   */
  async updateOrganization(data: UpdateOrganizationRequest): Promise<{ success: boolean; organization?: Organization }> {
    return http.post(ADMIN_ENDPOINTS.ORGANIZATION.UPDATE, data);
  },

  /**
   * Get organization configuration
   */
  async getOrganizationConfig(organizationId: string): Promise<OrganizationConfig> {
    return http.get(`${ADMIN_ENDPOINTS.ORGANIZATION.GET_CONFIG}?organization_id=${organizationId}`);
  },

  /**
   * Save organization configuration
   */
  async saveOrganizationConfig(data: SaveOrganizationConfigRequest): Promise<{ success: boolean }> {
    return http.post(ADMIN_ENDPOINTS.ORGANIZATION.SAVE_CONFIG, data);
  },
};
