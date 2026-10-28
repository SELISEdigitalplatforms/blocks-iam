const DISABLE_ERROR_MESSAGES: Record<string, string> = {
  invalid_username_password: "The password is incorrect.",
  invalid_mfa_code: "The code is wrong or has expired. Request a new code and try again.",
  mfa_disable_not_allowed: "Your organization's policy does not allow you to disable MFA.",
  account_locked: "Your account is locked because of too many attempts. Try again later.",
  mfa_not_enabled: "MFA is not enabled for this account.",
};

const FALLBACK_MESSAGE = "Could not disable MFA. Please try again.";

/** Maps the server error body ({ error, error_description }) to a message for the user. */
export const getDisableMfaErrorMessage = (error: unknown): string => {
  const errors = (error as { errors?: Record<string, unknown> } | null)?.errors;
  const code = typeof errors?.error === "string" ? errors.error : "";
  if (DISABLE_ERROR_MESSAGES[code]) return DISABLE_ERROR_MESSAGES[code];
  const description = errors?.error_description;
  return typeof description === "string" && description ? description : FALLBACK_MESSAGE;
};
