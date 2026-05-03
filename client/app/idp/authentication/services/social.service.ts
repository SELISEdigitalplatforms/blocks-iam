import { http } from "@/lib/http-client";
import {
  IDeleteSsoCredentialPayload,
  IDeleteSsoCredentialResponse,
  IGetOIDCCredentialResponse,
  IGetSsoCredentialByIdPayload,
  IGetSsoCredentialByIdResponse,
  IGetSsoCredentialsPayload,
  IGetSsoCredentialsResponse,
  ISaveSsoCredentialPayload,
  ISaveSsoCredentialResponse,
  IUpdateSsoCredentialStatusPayload,
  IUpdateSsoCredentialStatusResponse,
} from "@blocks-idp/authentication/models/sso.model";
import { SSO_ENDPOINTS, AUTH_OIDC_ENDPOINTS } from "../constants/endpoint.constant";

export class SSOService {
  getSsoCredentials(_payload?: IGetSsoCredentialsPayload): Promise<IGetSsoCredentialsResponse> {
    return http.get(SSO_ENDPOINTS.GET_SSO_CREDENTIALS);
  }

  getSsoCredentialId(
    payload: IGetSsoCredentialByIdPayload,
  ): Promise<IGetSsoCredentialByIdResponse> {
    return http.get(`${SSO_ENDPOINTS.GET_SSO_CREDENTIAL}?itemId=${payload.itemId}`);
  }

  saveSsoCredential(payload: ISaveSsoCredentialPayload): Promise<ISaveSsoCredentialResponse> {
    return http.post(SSO_ENDPOINTS.SAVE_SSO_CREDENTIAL, payload);
  }

  deleteSsoCredential(payload: IDeleteSsoCredentialPayload): Promise<IDeleteSsoCredentialResponse> {
    return http.post(SSO_ENDPOINTS.DELETE_SSO_CREDENTIAL, payload);
  }

  updateSsoCredentialStatus(
    payload: IUpdateSsoCredentialStatusPayload,
  ): Promise<IUpdateSsoCredentialStatusResponse> {
    return http.post(SSO_ENDPOINTS.UPDATE_STATUS, payload);
  }

  saveBlocksSsoCredential(payload: unknown): Promise<ISaveSsoCredentialResponse> {
    return http.post(AUTH_OIDC_ENDPOINTS.SAVE_OIDC_CLIENT, payload);
  }

  getBlocksSsoCredential(): Promise<IGetOIDCCredentialResponse> {
    return http.get(AUTH_OIDC_ENDPOINTS.GET_OIDC_CLIENT);
  }
}

export const ssoService = new SSOService();
