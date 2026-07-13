import { serviceInstances } from "@/lib/http-client";
import { DEVICE_ENDPOINTS } from "../constants/endpoints/device.endpoint";

export interface DeviceInteractionResponse {
  redirect: string;
  interactionId: string;
}

export interface DeviceConsentPayload {
  clientName: string;
  clientId: string;
  scopes: string[];
  tenant: string;
  userCode: string;
}

export interface DeviceApproveResponse {
  redirect: string;
  status: "Approved" | "Denied";
}

export interface DeviceErrorShape {
  error?: string;
  error_description?: string;
  status?: number;
}

const http = serviceInstances.idpService;

function tenantHeaders(tenantId: string): Record<string, string> | undefined {
  if (!tenantId) return undefined;
  return { "X-Blocks-Key": tenantId };
}

export const deviceService = {
  submitUserCode(userCode: string, tenantId: string): Promise<DeviceInteractionResponse> {
    return http.post<DeviceInteractionResponse>(
      DEVICE_ENDPOINTS.BEGIN,
      { user_code: userCode, tenant_id: tenantId || undefined },
      tenantHeaders(tenantId),
    ) as Promise<DeviceInteractionResponse>;
  },

  loadConsent(interactionId: string, tenantId: string): Promise<DeviceConsentPayload> {
    return http.get<DeviceConsentPayload>(
      DEVICE_ENDPOINTS.continue(interactionId),
      tenantHeaders(tenantId),
    ) as Promise<DeviceConsentPayload>;
  },

  approve(
    interactionId: string,
    decision: "allow" | "deny",
    tenantId: string,
  ): Promise<DeviceApproveResponse> {
    return http.post<DeviceApproveResponse>(
      DEVICE_ENDPOINTS.APPROVE,
      { interactionId, decision },
      tenantHeaders(tenantId),
    ) as Promise<DeviceApproveResponse>;
  },
};
