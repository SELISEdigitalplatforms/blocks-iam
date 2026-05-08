import { http } from '@/lib/http-client';
import {
  OidcClient,
  CreateOidcClientRequest,
  GetOidcClientsResponse,
  GetOidcClientResponse,
  CreateOidcClientResponse,
} from '@blocks-idp/shared/models/admin.models';
import { ADMIN_ENDPOINTS } from '@blocks-idp/admin/constants/endpoint.constant';

/**
 * OIDC Client Management Service
 * Handles CRUD operations for OIDC clients
 */
export const oidcClientService = {
  /**
   * Get list of OIDC clients
   */
  async getClients(): Promise<GetOidcClientsResponse> {
    return http.get(ADMIN_ENDPOINTS.OIDC_CLIENT.LIST);
  },

  /**
   * Get single OIDC client by ID
   */
  async getClient(clientId: string): Promise<GetOidcClientResponse> {
    return http.get(`${ADMIN_ENDPOINTS.OIDC_CLIENT.GET}/${clientId}`);
  },

  /**
   * Create new OIDC client
   */
  async createClient(data: CreateOidcClientRequest): Promise<CreateOidcClientResponse> {
    return http.post(ADMIN_ENDPOINTS.OIDC_CLIENT.CREATE, data);
  },

  /**
   * Update OIDC client
   */
  async updateClient(clientId: string, data: CreateOidcClientRequest): Promise<CreateOidcClientResponse> {
    return http.post(`${ADMIN_ENDPOINTS.OIDC_CLIENT.UPDATE}/${clientId}`, data);
  },

  /**
   * Delete OIDC client
   */
  async deleteClient(clientId: string): Promise<{ success: boolean }> {
    return http.delete(`${ADMIN_ENDPOINTS.OIDC_CLIENT.DELETE}/${clientId}`);
  },
};
