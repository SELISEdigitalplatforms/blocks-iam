export interface ISignupByEmailPayload {
  email: string;
  firstName: string;
  lastName: string;
  captchaCode: string;
  createOrganizationDuringSignup?: boolean;
  organizationName?: string;
  // Carried into the activation email so the user returns to the application they
  // signed up from rather than the IAM root login.
  clientId?: string;
  redirectUri?: string;
}
export interface ISignupByEmailResponse {
  itemId: string | null;
  errors: unknown | null;
  isSuccess: boolean;
}

export interface ISigninByEmailPayload {
  username: string;
  password: string;
   clientId?: string;
  state?: string;
  nonce?: string;
  scope?: string;
  redirectUri?: string;
  captchaCode?: string;
}
/**
 * An MFA challenge is delivered as a 200 whose body carries `error: "mfa_enabled"`
 * plus the handle needed to answer it. The HTTP client only rejects non-2xx, so a
 * challenge resolves like a success and callers must check `mfa_required` before
 * treating the response as a completed login.
 */
export interface IMfaChallengeFields {
  error?: string;
  error_description?: string;
  mfa_required?: boolean;
  mfa_id?: string;
  mfa_type?: number;
  mfa_methods?: string | null;
}

export interface ISigninByEmailResponse extends IMfaChallengeFields {
  access_token: string;
  token_type: string;
  expires_in: number;
  refresh_token: string;
}
export interface IVerifyMfaPayload {
  code: string;
  mfa_id: string;
  mfa_type: number;
}

export interface IVerifyMfaResponse {
  access_token: string;
  token_type: string;
  expires_in: number;
  refresh_token: string;
}

export interface LoginOptionSsoInfo {
  provider: string;
  audience: string;
  isAvailable?: boolean;
  [key: string]: unknown;
}

export interface LoginOption {
  allowedGrantTypes: string[];
  ssoInfo?: LoginOptionSsoInfo[];
  [key: string]: unknown;
}

export interface IActivateAccountPayload {
  code: string;
  password: string;
  captchaCode?: string;
  mailPurpose?: string;
  preventPostEvent?: boolean;
}

export interface IActivateAccountResponse {
  isSuccess: boolean;
  errors?: unknown;
}

export interface IRecoverAccountPayload {
  email: string;
  captchaCode?: string;
}

export interface IRecoverAccountResponse {
  isSuccess: boolean;
  errors?: unknown;
}
