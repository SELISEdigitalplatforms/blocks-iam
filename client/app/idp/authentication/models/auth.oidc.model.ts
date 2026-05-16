export interface IGetOidcPayload {
  clientId?: string;
}

export interface IOidcConfig {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  clientSecret: string;
  redirectUri: string;
  scope: string;
  audience: string;
  isAutoRedirect: boolean;
  useTokensCookie: boolean;
  tenantId: string;
  clientLogoUrl?: string;
  clientBrandColor?: string;
  clientDisplayName: string;
}
export interface IOidcConfigResponse {
  itemId: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  clientSecret: string;
  redirectUri: string;
  scope: string;
  audience: string;
  isAutoRedirect: boolean;
  useTokensCookie: boolean;
  tenantId: string;
  clientLogoUrl?: string;
  clientBrandColor?: string;
  clientDisplayName: string;
}

export interface ISaveOidcCredentialPayload {
  audience: string;
  isAutoRedirect: boolean;
  useTokensCookie: boolean;
  itemId: string;
  redirectUri: string;
  scope: string;
  clientLogoUrl?: string;
  clientBrandColor?: string;
  clientDisplayName: string;
}

export interface ISaveOidcCredentialResponse {
  audience: string;
  isAutoRedirect: boolean;
  useTokensCookie: boolean;
  itemId: string;
  redirectUri: string;
  scope: string;
  clientLogoUrl?: string;
  clientBrandColor?: string;
  clientDisplayName: string;
}

export interface IGetClientsPayload {}

export interface IClientCredentialsConfig {
  scope: string;
  itemId: string;
  name: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  clientSecret: string;
  roles: string[];
  isActive: boolean;
  audiences: string[];
}

export interface IClientConfigResponse {
  scope: string;
  itemId: string;
  name: string;
  createdDate: string;
  lastUpdatedDate: string;
  createdBy: string;
  language: string;
  lastUpdatedBy: string;
  organizationIds: string[];
  tags: string[];
  clientSecret: string;
  roles: string[];
  isActive: boolean;
  audiences: string[];
}

export interface ISaveClientCredentialPayload {
  name: string;
  roles: string[];
}

export interface ISaveClientCredentialResponse {
  name: string;
  roles: [];
}

export interface TabValue {
  tabValue: string;
}
export interface IDeleteOidcClientPayload {
  itemId: string | null;
}
export interface IDeleteOidcClientResponse {
  errors: {
    additionalProp1: string;
    additionalProp2: string;
    additionalProp3: string;
  };
  isSuccess: boolean;
}
