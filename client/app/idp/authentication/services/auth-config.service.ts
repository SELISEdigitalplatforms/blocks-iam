import { http } from "@/lib/http-client";
import {
  IAuthConfigPayload,
  IGetAuthConfigResponse,
  ISaveAuthConfigPayload,
  ISaveAuthConfigResponse,
} from "@blocks-idp/authentication/models/auth-configuration.model";
import { AUTH_CONFIG_ENDPOINTS } from "../constants/endpoint.constant";

export class AuthConfiguration {
  getConfig(_payload?: IAuthConfigPayload): Promise<IGetAuthConfigResponse> {
    return http.get(AUTH_CONFIG_ENDPOINTS.GET_CONFIG);
  }

  saveAuthConfig(payload: ISaveAuthConfigPayload): Promise<ISaveAuthConfigResponse> {
    return http.post(AUTH_CONFIG_ENDPOINTS.UPDATE_CONFIG, payload);
  }
}
