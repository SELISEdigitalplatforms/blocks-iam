import { serviceInstances } from "@/lib/http-client";
import { SECURITY_ENDPOINTS } from "../constants/security-endpoints";
import type {
  IActivityPageResponseApi,
  IGetActivitiesPayload,
  IGeneratePATPayload,
  IPATApi,
  IRevokeSessionResponseApi,
  ISecuritySummaryApi,
  ISessionDetailsApi,
  IUserSessionApi,
} from "../api";

const appendUid = (url: string, uid?: string) =>
  uid ? `${url}${url.includes("?") ? "&" : "?"}uid=${encodeURIComponent(uid)}` : url;

export class SecurityService {
  getSummary(uid?: string): Promise<ISecuritySummaryApi> {
    return serviceInstances.idpService.get<ISecuritySummaryApi>(
      appendUid(SECURITY_ENDPOINTS.SUMMARY, uid),
    );
  }

  getSessions(uid?: string): Promise<IUserSessionApi[]> {
    return serviceInstances.idpService.get<IUserSessionApi[]>(
      appendUid(SECURITY_ENDPOINTS.SESSIONS, uid),
    );
  }

  getSessionDetails(sessionId: string, uid?: string): Promise<ISessionDetailsApi> {
    return serviceInstances.idpService.get<ISessionDetailsApi>(
      appendUid(
        SECURITY_ENDPOINTS.SESSION_DETAILS.replace("{sessionId}", encodeURIComponent(sessionId)),
        uid,
      ),
    );
  }

  revokeSession(
    sessionId: string,
    reason?: string,
    userId?: string,
  ): Promise<IRevokeSessionResponseApi> {
    return serviceInstances.idpService.post<IRevokeSessionResponseApi>(
      SECURITY_ENDPOINTS.REVOKE_SESSION.replace("{sessionId}", encodeURIComponent(sessionId)),
      { ...(reason ? { reason } : {}), ...(userId ? { userId } : {}) },
    );
  }

  revokeRefreshToken(
    tokenId: string,
    reason?: string,
    userId?: string,
  ): Promise<IRevokeSessionResponseApi> {
    return serviceInstances.idpService.post<IRevokeSessionResponseApi>(
      SECURITY_ENDPOINTS.REVOKE_REFRESH_TOKEN.replace("{tokenId}", encodeURIComponent(tokenId)),
      { ...(reason ? { reason } : {}), ...(userId ? { userId } : {}) },
    );
  }

  getActivities(payload: IGetActivitiesPayload): Promise<IActivityPageResponseApi> {
    return serviceInstances.idpService.post<IActivityPageResponseApi>(
      SECURITY_ENDPOINTS.ACTIVITY,
      {
        ...(payload.userId ? { userId: payload.userId } : {}),
        ...(payload.page !== undefined ? { page: payload.page } : {}),
        ...(payload.pageSize !== undefined ? { pageSize: payload.pageSize } : {}),
        ...(payload.filter ? { filter: payload.filter } : {}),
      },
    );
  }

  getPats(): Promise<IPATApi[]> {
    return serviceInstances.idpService.get(SECURITY_ENDPOINTS.GET_USER_CODES);
  }

  generatePats(payload: IGeneratePATPayload): Promise<IPATApi[]> {
    return serviceInstances.idpService.post(SECURITY_ENDPOINTS.GENERATE_USER_CODE, payload);
  }
}

export const securityService = new SecurityService();