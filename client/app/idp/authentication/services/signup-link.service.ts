import { serviceInstances } from "@/lib/http-client";
import { SIGNUP_LINK_ENDPOINTS } from "../constants/endpoint.constant";

export type SignupLinkContext =
  | {
      valid: true;
      credentialMode: "Passwordless" | "PasswordRequired";
      firstName: string;
      maskedEmail: string;
      applicationName: string;
    }
  | { valid: false };

export type RedeemSignupLinkResponse = { authorizeUrl: string };

/**
 * Stateless signup-link API. The code is never placed in a URL path, query key,
 * localStorage, or log — only the X-Signup-Link-Code header or redeem body.
 */
export const signupLinkService = {
  getContext(code: string, tenantId?: string): Promise<SignupLinkContext> {
    const headers: Record<string, string> = {
      "X-Signup-Link-Code": code,
    };
    if (tenantId) {
      headers["X-Blocks-Key"] = tenantId;
    }
    return serviceInstances.idpService.get(
      SIGNUP_LINK_ENDPOINTS.CONTEXT,
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    ) as Promise<SignupLinkContext>;
  },

  redeem(code: string, tenantId?: string): Promise<RedeemSignupLinkResponse> {
    const headers: Record<string, string> = tenantId
      ? { "X-Blocks-Key": tenantId }
      : {};
    return serviceInstances.idpService.post(
      SIGNUP_LINK_ENDPOINTS.REDEEM,
      { code },
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    ) as Promise<RedeemSignupLinkResponse>;
  },
};
