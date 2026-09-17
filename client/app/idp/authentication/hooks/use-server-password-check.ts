import { useEffect, useRef, useState } from "react";
import { serviceInstances } from "@/lib/http-client";
import { PASSWORD_MAX_INPUT_LENGTH } from "@blocks-idp/authentication/utils/password-policy.util";
import { resolveTenantId } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";

const PASSWORD_CHECK_ENDPOINT = "/api/idp/password-check";

/** Long enough that a typist does not generate a request per keystroke. */
const DEBOUNCE_MS = 400;

/**
 * Asks the server whether a password satisfies a rule the client cannot evaluate -- one the config
 * endpoint marked `requiresServerCheck`, whose pattern is deliberately never published.
 *
 * Returns `undefined` until an answer for the current password arrives, so the caller can show the
 * requirement as neither met nor failed rather than flashing a wrong state while in flight.
 *
 * The password is sent over the same TLS connection that will carry it on submit, to the same
 * origin, and only while the user is typing into a password field on that origin. Requests are
 * debounced, the empty password is answered locally without a request, and a reply that arrives
 * after the password has moved on is discarded.
 */
export const useServerPasswordCheck = (
  password: string,
  enabled: boolean,
  tenantIdOverride?: string,
): boolean | undefined => {
  // Resolved exactly as the config endpoint's tenant is, so the rule checked here is the rule
  // whose policy the screen is showing.
  const tenantId = resolveTenantId(tenantIdOverride);
  const [result, setResult] = useState<{ password: string; meets: boolean } | null>(null);
  // Guards against an out-of-order reply overwriting a newer one.
  const latestRequest = useRef(0);

  useEffect(() => {
    if (!enabled) return;

    // Nothing typed yet is not a failure to report; it is simply not answered.
    if (password === "") {
      setResult(null);
      return;
    }

    const requestId = ++latestRequest.current;
    const timer = setTimeout(() => {
      const headers: Record<string, string> = tenantId ? { "X-Blocks-Key": tenantId } : {};

      void (serviceInstances.idpService.post(
        tenantId
          ? `${PASSWORD_CHECK_ENDPOINT}?tenantId=${encodeURIComponent(tenantId)}`
          : PASSWORD_CHECK_ENDPOINT,
        { password: password.slice(0, PASSWORD_MAX_INPUT_LENGTH) },
        headers,
        { absoluteUrl: true, skipBlocksKey: true },
      ) as Promise<{ meetsRequirements?: boolean }>)
        .then((response) => {
          if (requestId !== latestRequest.current) return;
          setResult({ password, meets: response?.meetsRequirements === true });
        })
        .catch(() => {
          // Unreachable or refused: leave the row unanswered rather than claiming a verdict.
          // The server still decides on submit.
          if (requestId === latestRequest.current) setResult(null);
        });
    }, DEBOUNCE_MS);

    return () => clearTimeout(timer);
  }, [password, enabled, tenantId]);

  if (!enabled) return undefined;
  return result?.password === password ? result.meets : undefined;
};
