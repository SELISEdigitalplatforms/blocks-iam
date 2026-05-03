import { http } from "@/lib/http-client";
import {
  IGetSocialLoginEndpointPayload,
  IGetSocialLoginEndpointResponse,
  ISigninBySSOPayload,
  ISigninBySSOResponse,
} from "@blocks-idp/authentication/models/oauth.model";
import { AUTH_ENDPOINTS } from "../constants/endpoint.constant";

export class OAuthService {
  getSocialLoginEndpoint(
    payload: IGetSocialLoginEndpointPayload,
  ): Promise<IGetSocialLoginEndpointResponse> {
    return http.post(AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT, payload);
  }

  signinBySSO(payload: ISigninBySSOPayload & { clientId?: string }): Promise<ISigninBySSOResponse> {
    return http.post(
      AUTH_ENDPOINTS.SOCIAL_LOGIN,
      {
        code: payload.code,
        state: payload.state,
        clientId: payload.clientId || "",
      },
      undefined,
      {
        skipTokenRotation: true,
      },
    );
  }
}

export const oauthService = new OAuthService();
