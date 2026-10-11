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

export type RedeemSignupLinkResponse = {
  authorizeUrl?: string;
  activationKey?: string;
  activationKeyExpiresAtUtc?: string;
  credentialMode?: "Passwordless" | "PasswordRequired";
  loginUrl?: string;
  mfaId?: string;
  userMfa?: string;
  error?: string;
  /** Set with error "authentication_required": the id the password step posts back. */
  redemptionId?: string;
  maskedEmail?: string;
  mode?: "Oidc" | "Embedded";
};

export type AuthenticateSignupLinkPayload = {
  redemptionId: string;
  password: string;
  captchaCode?: string;
};

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
    );
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
    );
  },

  authenticate(
    payload: AuthenticateSignupLinkPayload,
    tenantId?: string,
  ): Promise<RedeemSignupLinkResponse> {
    const headers: Record<string, string> = tenantId
      ? { "X-Blocks-Key": tenantId }
      : {};
    const body: Record<string, string> = {
      redemptionId: payload.redemptionId,
      password: payload.password,
    };
    if (payload.captchaCode) {
      body.captcha_code = payload.captchaCode;
    }
    return serviceInstances.idpService.post(
      SIGNUP_LINK_ENDPOINTS.REDEEM_AUTHENTICATE,
      body,
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    );
  },

  completeMfa(
    mfaId: string,
    mfaCode: string,
    tenantId?: string,
  ): Promise<RedeemSignupLinkResponse> {
    const headers: Record<string, string> = tenantId
      ? { "X-Blocks-Key": tenantId }
      : {};
    return serviceInstances.idpService.post(
      SIGNUP_LINK_ENDPOINTS.REDEEM_MFA,
      { mfaId, mfaCode },
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    );
  },
};
