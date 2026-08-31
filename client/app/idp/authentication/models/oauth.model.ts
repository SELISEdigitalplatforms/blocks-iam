import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";
import { IMfaChallengeFields } from "@blocks-idp/authentication/models/auth.model";

export interface IGetSocialLoginEndpointPayload {
  provider: SSO_PROVIDERS;
  audience: string;
  nextUrl?: string;
  sendAsResponse: boolean;
}
export interface IGetSocialLoginEndpointResponse {
  error: unknown;
  isAResponse: boolean;
  providerUrl: string;
}
export interface ISigninBySSOPayload {
  code: string;
  state: string;
}
export interface ISigninBySSOResponse extends IMfaChallengeFields {
  access_token: string;
  expires_in: number;
  refresh_token: string;
  token_type: string;
  sso_user_redirect_url?: string;
}
