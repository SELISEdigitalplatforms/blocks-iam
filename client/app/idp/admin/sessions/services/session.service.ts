import { http } from '@/lib/http-client';
import { Session, GetActivityRequest, GetActivityResponse } from '@blocks-idp/shared/models/admin.models';
import { ADMIN_ENDPOINTS } from '@blocks-idp/admin/constants/endpoint.constant';

/**
 * Session Management Service
 * Handles reading active sessions
 */
export const sessionService = {
  /**
   * Get user's active sessions
   */
  async getSessions(): Promise<{ sessions: Session[]; total: number }> {
    return http.get(ADMIN_ENDPOINTS.SESSION.LIST);
  },

  /**
   * Get activity history
   */
  async getActivityHistory(request: GetActivityRequest): Promise<GetActivityResponse> {
    const params = new URLSearchParams();
    if (request.page) params.set('page', String(request.page));
    if (request.page_size) params.set('page_size', String(request.page_size));
    if (request.action) params.set('action', request.action);
    if (request.sort_order) params.set('sort_order', request.sort_order);
    const qs = params.toString();
    return http.get(`${ADMIN_ENDPOINTS.SESSION.GET_HISTORY}${qs ? `?${qs}` : ''}`);
  },
};
