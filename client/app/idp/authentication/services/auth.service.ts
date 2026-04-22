import { http } from "@/lib/http-client";
import { useAuthStore } from "@/store/useAuthStore";
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
    const body = new URLSearchParams();
    body.append("grant_type", "password");
    body.append("username", payload.username);
    body.append("password", payload.password);

    return http.post(
      AUTH_ENDPOINTS.TOKEN,
      body,
      {
        "Content-Type": "application/x-www-form-urlencoded",
      },
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
    return http.post(AUTH_ENDPOINTS.TOKEN, body, {
      "Content-Type": "application/x-www-form-urlencoded",
    });
  }

  signupByEmail(payload: ISignupByEmailPayload): Promise<ISignupByEmailResponse> {
    return http.post(PEOPLE_ENDPOINTS.SIGNUP, payload);
  }

  getLoginOptions(): Promise<any> {
    return http.get(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
  }

  logout() {
    // For localhost, send actual refresh token; for remote, send empty (uses cookie)
    const isLocalhost = import.meta.env.BLOCKS_API_BASE_URL?.includes("localhost");
    const refreshToken = isLocalhost ? (useAuthStore.getState().refreshToken || "") : "";
    return http.post(AUTH_ENDPOINTS.LOGOUT, { refreshToken });
  }
}

export const authService = new AuthService();
