import { http } from "@/lib/http-client";

const IMPERSONATION_BASE = "/api/auth";

export interface ImpersonationRequest {
  targeted_tenant_id: string;
  orgId?: string;
  organizationId?: string;
}

export interface ImpersonationState {
  rootTenantId: string;
  targetTenantId: string;
  orgId: string;
  startedAtUtc: string;
}

export interface ImpersonationStatusResponse {
  impersonated: boolean;
  originalTenantId: string;
  impersonatedTenantId: string | null;
}

class ImpersonationService {
  startImpersonation(request: ImpersonationRequest): Promise<ImpersonationState> {
    return http.post(`${IMPERSONATION_BASE}/impersonate`, request);
  }

  stopImpersonation(): Promise<void> {
    return http.post(`${IMPERSONATION_BASE}/impersonation/stop`, null);
  }

  impersonationStatus(): Promise<ImpersonationStatusResponse> {
    return http.post(
      `${IMPERSONATION_BASE}/impersonation/status`,
      null,
      undefined,
      { skipTokenRotation: true },
    );
  }
}

export const impersonationService = new ImpersonationService();