import { serviceInstances } from "@/lib/http-client";
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
    return serviceInstances.idpService.post(AUTH_ENDPOINTS.SOCIAL_AUTHORIZE, payload);
  }

  signinBySSO(payload: ISigninBySSOPayload & { clientId?: string }): Promise<ISigninBySSOResponse> {
    return serviceInstances.idpService.post(
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
