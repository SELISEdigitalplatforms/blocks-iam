import { tStatic } from "@seliseblocks/genesis-os/i18n";

const IAM_SHARED_MODULE = "iam.shared";

const getKnownMessage = (code: string): string | undefined => {
  switch (code) {
    case "invalid_username_password":
      return tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.INVALID_USERNAME_PASSWORD");
    case "invalid_mfa_code":
      return tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.INVALID_MFA_CODE");
    case "mfa_disable_not_allowed":
      return tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.MFA_DISABLE_NOT_ALLOWED");
    case "account_locked":
      return tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.ACCOUNT_LOCKED");
    case "mfa_not_enabled":
      return tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.MFA_NOT_ENABLED");
    default:
      return undefined;
  }
};

/** Maps the server error body ({ error, error_description }) to a message for the user. */
export const getDisableMfaErrorMessage = (error: unknown): string => {
  const errors = (error as { errors?: Record<string, unknown> } | null)?.errors;
  const code = typeof errors?.error === "string" ? errors.error : "";
  const known = getKnownMessage(code);
  if (known) return known;
  const description = errors?.error_description;
  return typeof description === "string" && description ? description : tStatic(IAM_SHARED_MODULE, "MFA_DISABLE_ERRORS.FALLBACK");
};
