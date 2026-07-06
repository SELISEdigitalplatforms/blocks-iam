// ─── MFA configuration endpoints (mfa.service — cloud config) ──────────────

const MFA_CONFIG_SUBPATH = "/MFA";

export const MFA_CONFIG_ENDPOINTS = {
  GET: `/api${MFA_CONFIG_SUBPATH}/Config`,
  SAVE: `/api${MFA_CONFIG_SUBPATH}/Config`,
} as const;

// ─── MFA endpoints (mfa.service — IDP & MFA bases) ─────────────────────────

const MFA_SUBPATH = "/Mfa";
const MANAGEMENT_SUBPATH = "/Management";

export const MFA_ENDPOINTS = {
  GENERATE_OTP: `/api${MFA_SUBPATH}/Generate`,
  CONFIGURE_USER_MFA: `/api${MANAGEMENT_SUBPATH}/ConfigureUserMfa`,
  SETUP_TOTP: `/api${MFA_SUBPATH}/Totp/Setup`,
  VERIFY_OTP: `/api${MFA_SUBPATH}/Verify`,
  VERIFY_TOTP_SETUP: `/api${MFA_SUBPATH}/Totp/Verify-setup`,
  RESEND_OTP: `/api${MFA_SUBPATH}/Resend`,
  DISABLE_MFA: `/api${MFA_SUBPATH}/Disable`,
} as const;
