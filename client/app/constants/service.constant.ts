/**
 * How this app identifies itself to shared infrastructure.
 *
 * Declared once because more than one call site needs it and a mismatch would not fail loudly:
 * the Rollbar client is memoised on first use, so a second call passing a different name is
 * silently ignored and half the reports would be filed under the wrong service.
 */
export const SERVICE_NAME = "blocks-iam";
