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



// import { serviceInstances } from "@/lib/http-client";
// import {
//   IGetSocialLoginEndpointPayload,
//   IGetSocialLoginEndpointResponse,
//   ISigninBySSOPayload,
//   ISigninBySSOResponse,
// } from "@blocks-idp/authentication/models/oauth.model";
// import { GRANT_TYPES } from "../constants/authentication.constant";
// import { AUTH_ENDPOINTS, IDP_ENDPOINTS } from "../constants/endpoint.constant";

// export class OAuthService {
//   getSocialLoginEndpoint(
//     payload: IGetSocialLoginEndpointPayload,
//   ): Promise<IGetSocialLoginEndpointResponse> {
//     return serviceInstances.idpService.post(AUTH_ENDPOINTS.GET_SOCIAL_LOGIN_ENDPOINT, payload);
//   }

//   signinBySSO(payload: ISigninBySSOPayload): Promise<ISigninBySSOResponse> {
//     const body = new URLSearchParams();
//     body.append("grant_type", GRANT_TYPES.social);
//     body.append("code", payload.code);
//     body.append("state", payload.state);

//     return serviceInstances.idpService.post(
//       IDP_ENDPOINTS.AUTHENTICATION.TOKEN,
//       body,
//       {
//         "Content-Type": "application/x-www-form-urlencoded",
//       },
//       {
//         skipTokenRotation: true,
//       },
//     );
//   }
// }

// export const oauthService = new OAuthService();
