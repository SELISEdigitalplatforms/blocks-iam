import { tStatic } from "@seliseblocks/genesis-os/i18n";

const getKnownMessage = (code: string): string | undefined => {
  switch (code) {
    case "invalid_redirect_uri":
      return tStatic("iam.shared", "SSO_START_ERRORS.INVALID_REDIRECT_URI");
    case "invalid_client":
      return tStatic("iam.shared", "SSO_START_ERRORS.INVALID_CLIENT");
    case "provider_not_found":
      return tStatic("iam.shared", "SSO_START_ERRORS.PROVIDER_NOT_FOUND");
    case "provider_inactive":
      return tStatic("iam.shared", "SSO_START_ERRORS.PROVIDER_INACTIVE");
    case "invalid_oidc_state":
      return tStatic("iam.shared", "SSO_START_ERRORS.INVALID_OIDC_STATE");
    case "authorization_url_generation_failed":
      return tStatic("iam.shared", "SSO_START_ERRORS.AUTHORIZATION_URL_GENERATION_FAILED");
    default:
      return undefined;
  }
};

const getFallbackMessage = () => tStatic("iam.shared", "SSO_START_ERRORS.FALLBACK");

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
  const known = getKnownMessage(code);
  if (known) return known;

  const description = (body as { error_description?: unknown } | null)?.error_description;
  if (typeof description === "string" && description) return description;
  if (code) return code;
  return typeof thrown === "string" && thrown ? thrown : getFallbackMessage();
};
