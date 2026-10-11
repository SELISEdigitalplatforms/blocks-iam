import { mockSuccessResponse, mockErrorResponse } from "@/test-utils/__mocks__";
import type { ISigninByEmailPayload } from "../../authentication/models/auth.model";
import type {
  ISignupByEmailPayload,
  ISignupByEmailResponse,
} from "../../authentication/models/auth.model";
import type { IVerifyMfaPayload, IVerifyMfaResponse } from "../../authentication/models/auth.model";
import type {
  IGetSocialLoginEndpointPayload,
  ISigninBySSOPayload,
} from "../../authentication/models/oauth.model";

export { mockSuccessResponse, mockErrorResponse };

// ─── Mock IDs ─────────────────────────────────────────────────────────────────

export const MOCK_USER_ID = "user-m3n4-o5p6";

// ─── Auth Mocks ───────────────────────────────────────────────────────────────

export const mockSigninPayload: ISigninByEmailPayload = {
  username: "test@blocks.com",
  password: "Test@1234",
};

export const mockSigninResponse = {
  access_token: "mock-access-token",
  token_type: "Bearer",
  expires_in: 3600,
  refresh_token: "mock-refresh-token",
};

export const mockSignupPayload: ISignupByEmailPayload = {
  email: "newuser@blocks.com",
  firstName: "New",
  lastName: "User",
  captchaCode: "captcha-code",
};

export const mockSignupResponse: ISignupByEmailResponse = {
  itemId: MOCK_USER_ID,
  errors: null,
  isSuccess: true,
};

export const mockVerifyMfaPayload: IVerifyMfaPayload = {
  code: "123456",
  mfa_id: "mfa-id-123",
  mfa_type: 1,
};

export const mockVerifyMfaResponse: IVerifyMfaResponse = {
  access_token: "mock-access-token-after-mfa",
  token_type: "Bearer",
  expires_in: 3600,
  refresh_token: "mock-refresh-token-after-mfa",
};

// ─── OAuth / SSO Mocks ───────────────────────────────────────────────────────

export const mockGetSocialLoginPayload: IGetSocialLoginEndpointPayload = {
  provider: "google" as never,
  audience: "blocks-cloud",
  sendAsResponse: false,
};

export const mockGetSocialLoginResponse = {
  error: null,
  isAResponse: false,
  providerUrl: "https://accounts.google.com/o/oauth2/v2/auth?client_id=123",
};

export const mockSigninBySSOPayload: ISigninBySSOPayload = {
  code: "sso-auth-code",
  state: "sso-state-token",
};

export const mockSigninBySSOResponse = {
  access_token: "mock-sso-access-token",
  expires_in: 3600,
  refresh_token: "mock-sso-refresh-token",
  token_type: "Bearer",
  mfa_required: false,
};
