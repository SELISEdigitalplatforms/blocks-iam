// ─── MFA configuration endpoints (mfa.service — cloud config) ──────────────

const MFA_CONFIG_SUBPATH = "/MFA";

export const MFA_CONFIG_ENDPOINTS = {
  GET: `/api${MFA_CONFIG_SUBPATH}/Get`,
  SAVE: `/api${MFA_CONFIG_SUBPATH}/Save`,
} as const;

// ─── MFA endpoints (mfa.service — IDP & MFA bases) ─────────────────────────

const MFA_SUBPATH = "/Mfa";
const MANAGEMENT_SUBPATH = "/Management";

export const MFA_ENDPOINTS = {
  GENERATE_OTP: `/api${MFA_SUBPATH}/GenerateOTP`,
  CONFIGURE_USER_MFA: `/api${MANAGEMENT_SUBPATH}/ConfigureUserMfa`,
  SETUP_TOTP: `/api${MFA_SUBPATH}/SetUpTotp`,
  VERIFY_OTP: `/api${MFA_SUBPATH}/VerifyOTP`,
  RESEND_OTP: `/api${MFA_SUBPATH}/ResendOtp`,
  DISABLE_MFA: `/api${MFA_SUBPATH}/DisableUserMfa`,
} as const;
