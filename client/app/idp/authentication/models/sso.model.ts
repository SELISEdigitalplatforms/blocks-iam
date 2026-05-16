import { SSO_PROVIDERS } from "@blocks-idp/authentication/constants/sso-providers.constant";

export interface ISsoProviderConfiguration {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  provider: SSO_PROVIDERS;
  audience: string;
  clientId: string;
  clientSecret: string;
  authorizationUrl: string;
  tokenUrl: string;
  getProfileUrl: string;
  redirectUrl: string;
  scope: string[];
  initialRoles: string[];
  initialPermissions: string[];
  isDisabled: boolean;
  userRoles: { id: string; name: string; [key: string]: unknown }[];
  userPermissions: { id: string; name: string; [key: string]: unknown }[];
  isAutoRedirect?: boolean;
  wellKnownUrl?: string;
}

export interface ISsoProviderFrontendMeta {
  label: string;
  description: string;
  isConfigured?: boolean;
  imageSrc: string;
  imageSrcDark?: string;
  isAvailable?: boolean;
}

export type ISsoProviderConfigurationWithMeta = ISsoProviderConfiguration &
  ISsoProviderFrontendMeta;

export interface ISaveSsoCredentialPayload {
  itemId?: string;
  provider: string;
  audience: string;
  clientId: string;
  clientSecret: string;
  redirectUrl: string;
  initialRoles: string[];
  initialPermissions: string[];
}

export interface ISaveSsoCredentialResponse {
  isSuccess: boolean;
  errors: unknown;
  itemId: string;
}

export interface IDeleteSsoCredentialPayload {
  itemId: string;
}

export interface IDeleteSsoCredentialResponse {
  isSuccess: boolean;
  errors: unknown;
}
export interface IGetSsoCredentialByIdPayload {
  itemId: string;
}

export interface IGetSsoCredentialByIdResponse extends ISsoProviderConfiguration {}
export interface IGetSsoCredentialsPayload {}

export type IGetSsoCredentialsResponse = ISsoProviderConfiguration[];

export interface IUpdateSsoCredentialStatusPayload {
  itemId: string;
  isEnabled: boolean;
}

export interface IUpdateSsoCredentialStatusResponse {
  isSuccess: boolean;
  errors: unknown;
}

export interface IGetOIDCCredentialResponse {
  audience?: string;
  itemId?: string;
  clientId?: string;
  clientSecret?: string;
  redirectUri?: string;
  isAutoRedirect?: boolean;
  scope?: string;
}
