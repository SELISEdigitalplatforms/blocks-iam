import { serviceInstances } from "@/lib/http-client";
import { DEVICE_ENDPOINTS } from "../constants/endpoints/device.endpoint";

export interface DeviceConsentPayload {
  clientName: string;
  clientId: string;
  scopes: string[];
  tenant: string;
  userCode: string;
}

export interface DeviceVerifyResponse {
  status: "ready" | "login_required";
  payload?: DeviceConsentPayload;
  returnUrl?: string;
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
  verify(userCode: string, tenantId: string): Promise<DeviceVerifyResponse> {
    return http.post<DeviceVerifyResponse>(
      DEVICE_ENDPOINTS.VERIFY,
      { user_code: userCode },
      tenantHeaders(tenantId),
    ) as Promise<DeviceVerifyResponse>;
  },

  decide(
    userCode: string,
    decision: "allow" | "deny",
    tenantId: string,
  ): Promise<DeviceApproveResponse> {
    return http.post<DeviceApproveResponse>(
      DEVICE_ENDPOINTS.DECISION,
      { user_code: userCode, decision },
      tenantHeaders(tenantId),
    ) as Promise<DeviceApproveResponse>;
  },
};
