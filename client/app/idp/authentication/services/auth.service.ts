import { serviceInstances } from "@/lib/http-client";
import {
  ISigninByEmailPayload,
  ISigninByEmailResponse,
  ISignupByEmailPayload,
  ISignupByEmailResponse,
  IVerifyMfaPayload,
  IVerifyMfaResponse,
  IActivateAccountPayload,
  IActivateAccountResponse,
  IRecoverAccountPayload,
  IRecoverAccountResponse,
} from "@blocks-idp/authentication/models/auth.model";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";

export class AuthService {
  signinByEmail(
    payload: ISigninByEmailPayload,
  ): Promise<ISigninByEmailResponse> {
    if (payload.clientId) {
      sessionStorage.setItem("blocks-auth-client-id", payload.clientId);
    } else {
      sessionStorage.removeItem("blocks-auth-client-id");
    }

    return serviceInstances.idpService.post(
      AUTH_ENDPOINTS.LOGIN,
      {
        username: payload.username,
        password: payload.password,
        clientId: payload.clientId || "",
        ...(payload.captchaCode ? { captchaCode: payload.captchaCode } : {}),
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
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.OIDC_TOKEN, body, {
      "Content-Type": "application/x-www-form-urlencoded",
    });
  }

  verifyOidc(payload: {
    code: string;
    clientId?: string;
    redirectUri?: string;
    codeVerifier?: string;
    tenantId?: string;
    state?: string;
  }): Promise<any> {
    const body: any = {
      code: payload.code,
    };

    // If state is provided, backend will use it to retrieve code_verifier from cache
    if (payload.state) {
      body.state = payload.state;
    } else {
      // Legacy support: send clientId, redirectUri, codeVerifier if no state
      if (payload.clientId) body.client_id = payload.clientId;
      if (payload.redirectUri) body.redirect_uri = payload.redirectUri;
      if (payload.codeVerifier) body.code_verifier = payload.codeVerifier;
    }

    if (payload.tenantId) {
      body.tenant_id = payload.tenantId;
    }

    return serviceInstances.idpService.post(AUTH_ENDPOINTS.TOKEN_EXCHANGE, body);
  }

  verifySsoConsent(code: string): Promise<any> {
    const body = new URLSearchParams();
    body.append("grant_type", "sso_consent");
    body.append("code", code);

    return serviceInstances.idpService.post(AUTH_ENDPOINTS.OIDC_TOKEN, body, {
      "Content-Type": "application/x-www-form-urlencoded",
    });
  }

  signupByEmail(
    payload: ISignupByEmailPayload,
    tenantId?: string,
  ): Promise<ISignupByEmailResponse> {
    const headers: Record<string, string> = {};
    if (tenantId) {
      headers["X-Blocks-Key"] = tenantId;
    }
    return serviceInstances.idpService.post(
      AUTH_ENDPOINTS.SIGNUP,
      {
        ...payload,
        isSsoSignup: false,
      },
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    );
  }

  activateAccount(
    payload: IActivateAccountPayload,
  ): Promise<IActivateAccountResponse> {
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.ACTIVATE_ACCOUNT, payload);
  }

  recoverAccount(
    payload: IRecoverAccountPayload,
  ): Promise<IRecoverAccountResponse> {
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.RECOVER, payload);
  }

  getLoginOptions(tenantId?: string): Promise<any> {
    const url = tenantId
      ? `${AUTH_ENDPOINTS.GET_LOGIN_OPTIONS}?tenantId=${encodeURIComponent(tenantId)}`
      : AUTH_ENDPOINTS.GET_LOGIN_OPTIONS;
    const headers: Record<string, string> = tenantId
      ? { "X-Blocks-Key": tenantId }
      : {};
    return serviceInstances.idpService.get(
      url,
      headers,
      tenantId ? { skipBlocksKey: true } : undefined,
    );
  }

  logout() {
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.LOGOUT, {});
  }

  stopImpersonation(): Promise<{
    mode: "root" | "impersonation";
    status: string;
    reason?: string;
  }> {
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.STOP_IMPERSONATION, {});
  }

  startImpersonation(payload: {
    targeted_tenant_id: string;
    orgId?: string;
    clientId?: string;
  }): Promise<any> {
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.IMPERSONATE, payload);
  }

  signinByOidcEmail(payload: {
    provider?: string;
    clientId: string;
    redirectUri: string;
    scope?: string;
    state?: string;
    nonce?: string;
    code_challenge?: string;
    code_challenge_method?: string;
    tenantId?: string;
    provider_client_id: string;
    provider_redirect_uri: string;
  }): Promise<any> {
    const tenantId = payload.tenantId?.trim();
    
    const headers: Record<string, string> = tenantId
      ? { "X-Blocks-Key": tenantId }
      : {};
    return serviceInstances.idpService.post(
      AUTH_ENDPOINTS.OIDC_LOGIN,
      {
        ...(payload.provider && { provider: payload.provider }),
        client_id: payload.clientId,
        redirect_uri: payload.redirectUri,
        scope: payload.scope,
        state: payload.state,
        nonce: payload.nonce,
        code_challenge: payload.code_challenge,
        code_challenge_method: payload.code_challenge_method,
        tenant_id: tenantId,
        provider_client_id: payload.provider_client_id,
        provider_redirect_uri: payload.provider_redirect_uri,

      },
      headers,
      {
        absoluteUrl: true,
        skipTokenRotation: true,
        skipBlocksKey: true,
      },
    );
  }

  selectOidcAccount(payload: {
    userId: string;
    tenantId: string;
    clientId: string;
    redirectUri: string;
    scope?: string;
    state?: string;
    nonce?: string;
    code_challenge?: string;
    code_challenge_method?: string;
  }): Promise<any> {
    const headers: Record<string, string> = payload.tenantId
      ? { "X-Blocks-Key": payload.tenantId }
      : {};
    return serviceInstances.idpService.post(
      AUTH_ENDPOINTS.OIDC_LOGIN_SELECT_ACCOUNT,
      {
        user_id: payload.userId,
        tenant_id: payload.tenantId,
        client_id: payload.clientId,
        redirect_uri: payload.redirectUri,
        scope: payload.scope,
        state: payload.state,
        nonce: payload.nonce,
        code_challenge: payload.code_challenge,
        code_challenge_method: payload.code_challenge_method,
      },
      headers,
      {
        skipTokenRotation: true,
        skipBlocksKey: true,
      },
    );
  }
}

export const authService = new AuthService();

// import { serviceInstances } from "@/lib/http-client";
// import { getRuntimeEnv } from "@/lib/runtime-env";
// import { useAuthStore } from "@seliseblocks/genesis-os";
// import {
//   ISigninByEmailPayload,
//   ISigninByEmailResponse,
//   ISignupByEmailPayload,
//   ISignupByEmailResponse,
//   IVerifyMfaPayload,
//   IVerifyMfaResponse,
// } from "@blocks-idp/authentication/models/auth.model";
// import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";
// import { PEOPLE_ENDPOINTS } from "@blocks-identifier/constants/endpoint.constant";

// export class AuthService {
//   signinByEmail(payload: ISigninByEmailPayload): Promise<ISigninByEmailResponse> {
//     const body = new URLSearchParams();
//     body.append("grant_type", "password");
//     body.append("username", payload.username);
//     body.append("password", payload.password);

//     return serviceInstances.idpService.post(
//       AUTH_ENDPOINTS.TOKEN,
//       body,
//       {
//         "Content-Type": "application/x-www-form-urlencoded",
//       },
//       {
//         skipTokenRotation: true,
//       },
//     );
//   }

//   verifyMfa(payload: IVerifyMfaPayload): Promise<IVerifyMfaResponse> {
//     const body = new URLSearchParams();
//     body.append("grant_type", "mfa_code");
//     body.append("code", payload.code);
//     body.append("mfa_id", payload.mfa_id);
//     body.append("mfa_type", payload.mfa_type.toString());
//     return serviceInstances.idpService.post(AUTH_ENDPOINTS.TOKEN, body, {
//       "Content-Type": "application/x-www-form-urlencoded",
//     });
//   }

//   verifyOidc(payload: { code: string; state: string }): Promise<any> {
//     const body = new URLSearchParams();
//     body.append("grant_type", "authorization_code");
//     body.append("code", payload.code);
//     body.append("state", payload.state);
//     body.append("client_secret", "***REMOVED***");

//     return serviceInstances.idpService.post(
//       `https://dev-idp.blocksdevelopers.com${AUTH_ENDPOINTS.TOKEN}`,
//       body,
//       {
//         "Content-Type": "application/x-www-form-urlencoded",
//         "Authorization": "Basic c2VsaXNlYmxvY2tzOkJsMDNrc0B1JFU3VjEwUw=="
//       },
//       {
//         absoluteUrl: true,

//       },
//     );
//   }

//   signupByEmail(payload: ISignupByEmailPayload): Promise<ISignupByEmailResponse> {
//     return serviceInstances.idpService.post(PEOPLE_ENDPOINTS.SIGNUP, payload);
//   }

//   getLoginOptions(): Promise<any> {
//     return serviceInstances.idpService.get(AUTH_ENDPOINTS.GET_LOGIN_OPTIONS);
//   }

//   logout() {
//     // For localhost, send actual refresh token; for remote, send empty (uses cookie)
//     const isLocalhost = getRuntimeEnv("BLOCKS_IAM_BASE_URL")?.includes("localhost");
//     const refreshToken = isLocalhost ? (useAuthStore.getState().refreshToken || "") : "";
//     return serviceInstances.idpService.post(AUTH_ENDPOINTS.LOGOUT, { refreshToken });
//   }
// }

// export const authService = new AuthService();
