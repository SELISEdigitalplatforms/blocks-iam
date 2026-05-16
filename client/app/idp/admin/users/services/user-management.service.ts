import { http } from '@/lib/http-client';
import {
  User,
  CreateUserRequest,
  UpdateUserRequest,
  GetUsersRequest,
  GetUsersResponse,
  GetUserResponse,
  DeactivateUserRequest,
  CreateUserResponse,
} from '@blocks-idp/shared/models/admin.models';
import { ADMIN_ENDPOINTS } from '@blocks-idp/admin/constants/endpoint.constant';

/**
 * User Management Service
 * Handles CRUD operations for users in IDP Admin
 */
export const userManagementService = {
  /**
   * Get paginated list of users
   */
  async getUsers(query: GetUsersRequest): Promise<GetUsersResponse> {
    return http.post(ADMIN_ENDPOINTS.USER.LIST, query);
  },

  /**
   * Get single user by ID
   */
  async getUser(userId: string): Promise<GetUserResponse> {
    return http.get(`${ADMIN_ENDPOINTS.USER.GET}?id=${userId}`);
  },

  /**
   * Create new user
   */
  async createUser(data: CreateUserRequest): Promise<CreateUserResponse> {
    return http.post(ADMIN_ENDPOINTS.USER.CREATE, data);
  },

  /**
   * Update user information
   */
  async updateUser(data: UpdateUserRequest): Promise<CreateUserResponse> {
    return http.post(ADMIN_ENDPOINTS.USER.UPDATE, data);
  },

  /**
   * Deactivate user account
   */
  async deactivateUser(userId: string): Promise<{ success: boolean; message?: string }> {
    const request: DeactivateUserRequest = { user_id: userId };
    return http.post(ADMIN_ENDPOINTS.USER.DEACTIVATE, request);
  },

  /**
   * Check if email is available
   */
  async checkEmailAvailability(email: string): Promise<{ is_available: boolean }> {
    return http.get(`${ADMIN_ENDPOINTS.USER.CHECK_EMAIL}?email=${encodeURIComponent(email)}`);
  },

  /**
   * Get user activity timeline (audit log)
   */
  async getUserTimelines(
    userId: string,
    options?: { page?: number; page_size?: number }
  ): Promise<unknown> {
    const params = new URLSearchParams({ user_id: userId, ...options as Record<string, string> });
    return http.get(`${ADMIN_ENDPOINTS.USER.GET_TIMELINES}?${params.toString()}`);
  },
};
