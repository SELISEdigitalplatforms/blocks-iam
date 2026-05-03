import { http } from "@/lib/http-client";
import {
  ISigninByEmailPayload,
  ISigninByEmailResponse,
  ISignupByEmailPayload,
  ISignupByEmailResponse,
  IVerifyMfaPayload,
  IVerifyMfaResponse,
} from "@blocks-idp/authentication/models/auth.model";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

export class AuthService {
  signinByEmail(payload: ISigninByEmailPayload): Promise<ISigninByEmailResponse> {
    if (payload.clientId) {
      sessionStorage.setItem("blocks-auth-client-id", payload.clientId);
    } else {
      sessionStorage.removeItem("blocks-auth-client-id");
    }

    return http.post(
      AUTH_ENDPOINTS.LOGIN,
      {
        username: payload.username,
        password: payload.password,
        clientId: payload.clientId || "",
      },
      undefined,
      {
        skipTokenRotation: true,
      },
    );
  }

  verifyMfa(payload: IVerifyMfaPayload): Promise<IVerifyMfaResponse> {
    const body = new URLSearchParams();
    body.append("grant_type", "mfa_code");
    body.append("code", payload.code);
    body.append("mfa_id", payload.mfa_id);
    body.append("mfa_type", payload.mfa_type.toString());
    return http.post(AUTH_ENDPOINTS.LEGACY_TOKEN, body, {
      "Content-Type": "application/x-www-form-urlencoded",
    });
  }

  verifyOidc(payload: { code: string; clientId: string; redirectUri: string; codeVerifier: string; tenantId?: string }): Promise<any> {
    const body = new URLSearchParams();
    body.append("grant_type", "authorization_code");
    body.append("code", payload.code);
    body.append("client_id", payload.clientId);
    body.append("redirect_uri", payload.redirectUri);
    body.append("code_verifier", payload.codeVerifier);
    if (payload.tenantId) {
      body.append("tenant_id", payload.tenantId);
    }

    return http.post(
      AUTH_ENDPOINTS.OIDC_TOKEN,
      body,
      {
        "Content-Type": "application/x-www-form-urlencoded",
      },
    );
  }

  signupByEmail(payload: ISignupByEmailPayload): Promise<ISignupByEmailResponse> {
    return http.post(PEOPLE_ENDPOINTS.SIGNUP, payload);
  }

  getLoginOptions(): Promise<any> {
    return http.get(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
  }

  logout() {
    return http.post(AUTH_ENDPOINTS.LOGOUT, {});
  }

  stopImpersonation(): Promise<{ mode: "root" | "impersonation"; status: string; reason?: string }> {
    return http.post(AUTH_ENDPOINTS.STOP_IMPERSONATION, {});
  }

  startImpersonation(payload: { targetTenantId: string; orgId?: string; clientId?: string }): Promise<any> {
    return http.post(AUTH_ENDPOINTS.IMPERSONATE, payload);
  }
}

export const authService = new AuthService();
