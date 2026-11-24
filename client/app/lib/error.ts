import { tStatic } from "@seliseblocks/genesis-os/i18n";

const IAM_SHARED_MODULE = "iam.shared";

export const getErrorMessage = (
  error: Record<string, string | string[]>,
  messageMap: Record<string, string> = {},
): string | string[] => {
  if (!error || Object.keys(error).length === 0) {
    return tStatic(IAM_SHARED_MODULE, "ERROR.SOMETHING_WENT_WRONG");
  }

  const messages: string[] = [];

  for (const key in error) {
    const value = error[key];

    if (messageMap[key]) {
      messages.push(messageMap[key]);
      continue;
    }

    if (typeof value === "string") {
      messages.push(value);
    } else if (Array.isArray(value) && value.length > 0) {
      messages.push(value.join(", "));
    }
  }

  return messages.length ? messages : tStatic(IAM_SHARED_MODULE, "ERROR.SOMETHING_WENT_WRONG");
};

/**
 * True for a request that never got an answer (offline, DNS, CORS, blocked or aborted).
 *
 * Since genesis-os 4.5.1 (pulled in by #688) the HttpClient turns the thrown fetch error into
 * `HttpError(500, { errors: { general: <text>, message: <text> } })`, both set to the browser's own
 * text: "Failed to fetch" (Chromium), "NetworkError when attempting to fetch resource." (Firefox),
 * "Load failed" (Safari), or "Network request failed" when there is none. That text is English and
 * not meant for users, so the forms show their own translated message instead. A server answer is
 * passed through as its body, so it never has exactly this shape.
 */
export const isNetworkError = (error: unknown): boolean => {
  if (typeof error !== "object" || error === null) return false;
  const { status, errors } = error as { status?: unknown; errors?: unknown };
  if (status !== undefined && status !== 500) return false;
  if (!errors || typeof errors !== "object" || Array.isArray(errors)) return false;
  const fields = errors as Record<string, unknown>;
  return (
    Object.keys(fields).length === 2 &&
    typeof fields.general === "string" &&
    fields.general === fields.message
  );
};

/**
 * True for an error that carries the server's `errors`. A network error ({@link isNetworkError}) is
 * not one: its `errors` hold the browser's text, so callers fall back to their translated message.
 */
export const isErrorWithErrors = (
  error: unknown,
): error is { errors: Record<string, string | string[]> } => {
  return (
    typeof error === "object" &&
    error !== null &&
    "errors" in error &&
    typeof (error as { errors: unknown }).errors === "object" &&
    !isNetworkError(error)
  );
};

export const handleErrorMessages = (
  errors: unknown,
  customMessages?: Record<string, string>,
): string | string[] => {
  if (typeof errors === "string") return errors;

  if (errors && typeof errors === "object" && !Array.isArray(errors)) {
    return getErrorMessage(errors as Record<string, string | string[]>, customMessages);
  }

  return tStatic(IAM_SHARED_MODULE, "ERROR.UNEXPECTED");
};

/**
 * The first message of a server `errors` value: an array's first entry, an object's first
 * value, or the string itself; `fallback` when there is none.
 */
export const firstErrorText = (errors: unknown, fallback: string): string => {
  if (Array.isArray(errors)) return errors[0] as string;
  if (errors && typeof errors === "object") {
    return Object.values(errors as Record<string, string>)[0] ?? fallback;
  }
  return (errors as string) || fallback;
};
