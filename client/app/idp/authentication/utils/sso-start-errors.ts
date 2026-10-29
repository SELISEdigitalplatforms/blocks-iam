const SSO_START_ERROR_MESSAGES: Record<string, string> = {
  invalid_redirect_uri:
    "This sign-in option is not set up for this page. Contact your administrator.",
  invalid_client: "The application that sent you here is not recognised.",
  provider_not_found: "This sign-in option is not available. Contact your administrator.",
  provider_inactive: "This sign-in option is turned off. Contact your administrator.",
  invalid_oidc_state: "Your sign-in session has expired. Please start again.",
  authorization_url_generation_failed:
    "Could not start sign-in with this provider. Please try again.",
};

const FALLBACK_MESSAGE = "Could not start sign-in with this provider. Please try again.";

const readCode = (body: unknown): string => {
  const error = (body as { error?: unknown } | null)?.error;
  return typeof error === "string" ? error : "";
};

/**
 * Maps the error from starting a social sign-in to a message for the user. Takes either the
 * response body ({ error, error_description }) or a thrown error that carries it in `errors`.
 */
export const getSsoStartErrorMessage = (error: unknown): string => {
  const thrown = (error as { errors?: unknown } | null)?.errors;
  const body = thrown && typeof thrown === "object" ? thrown : error;
  const code = readCode(body);
  if (SSO_START_ERROR_MESSAGES[code]) return SSO_START_ERROR_MESSAGES[code];

  const description = (body as { error_description?: unknown } | null)?.error_description;
  if (typeof description === "string" && description) return description;
  if (code) return code;
  return typeof thrown === "string" && thrown ? thrown : FALLBACK_MESSAGE;
};
