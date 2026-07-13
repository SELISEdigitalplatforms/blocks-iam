import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { Loader } from "lucide-react";

import { OidcAuthShell, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { DEVICE_ENTRY_PANEL } from "./panel-config";
import {
  formatUserCodeForDisplay,
  isValidUserCode,
  normalizeUserCode,
} from "@blocks-idp/authentication/utils/device-utils";
import { deviceService } from "@blocks-idp/authentication/services/device.service";
import { buildOIDCNavigationUrl } from "@blocks-idp/authentication/utils/oidc-utils";

export function DeviceEntryPage() {
  const params = useParams<{ tenantId: string }>();
  const [searchParams] = useSearchParams();
  const tenantId = (params.tenantId ?? "").trim();
  const animCtx = useOidcAuthAnimation();

  const initialCode = useMemo(() => {
    const fromQuery = searchParams.get("user_code") ?? "";
    return fromQuery ? formatUserCodeForDisplay(fromQuery) : "";
  }, [searchParams]);

  const [value, setValue] = useState(initialCode);
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const formRef = useRef<HTMLFormElement>(null);
  const autoSubmittedRef = useRef(false);

  const invalidTenant = !tenantId;

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("oidc-animate-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("oidc-animate-shake");
  }

  useEffect(() => {
    if (invalidTenant || !initialCode || autoSubmittedRef.current) return;
    if (!isValidUserCode(initialCode)) return;
    autoSubmittedRef.current = true;
    void runSubmit(normalizeUserCode(initialCode));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialCode, invalidTenant]);

  async function runSubmit(code: string) {
    setServerError(null);
    setIsSubmitting(true);
    animCtx?.startAnimation();
    try {
      const res = await deviceService.submitUserCode(code, tenantId);
      await animCtx?.succeedAnimation();
      window.location.assign(res.redirect);
    } catch {
      shake();
      const msg = "Invalid or expired code.";
      setServerError(msg);
      await animCtx?.failAnimation(msg);
      setIsSubmitting(false);
    }
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || invalidTenant) return;
    const code = normalizeUserCode(value);
    if (!isValidUserCode(code)) {
      shake();
      const msg = "Enter the 8-character verification code shown on your device.";
      setServerError(msg);
      void animCtx?.failAnimation(msg);
      return;
    }
    void runSubmit(code);
  }

  function onChange(e: React.ChangeEvent<HTMLInputElement>) {
    setValue(formatUserCodeForDisplay(e.target.value));
  }

  if (invalidTenant) {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_ENTRY_PANEL}
        heading="Device Verification"
        headingDimFirst={2}
        showCorners={false}
        footerNote={
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            <Link to={buildOIDCNavigationUrl("/oidc/login")} className="oidc-sci-fi-link">
              Back to sign-in
            </Link>
          </p>
        }
      >
        <div className="flex flex-col gap-3">
          <div
            className="rounded-lg p-4"
            style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
          >
            <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
              Missing Tenant
            </p>
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              This URL is missing a tenant. Open the link exactly as displayed on your device.
            </p>
          </div>
        </div>
      </OidcAuthShell>
    );
  }

  const isAuthenticating =
    isSubmitting ||
    animCtx?.phase === "submitting" ||
    animCtx?.phase === "succeeded";

  return (
    <OidcAuthShell
      panelConfig={DEVICE_ENTRY_PANEL}
      heading="Enter your device code"
      headingDimFirst={3}
      showCorners={false}
      footerNote={
        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          © {new Date().getFullYear()} SELISE Digital Platforms. Secure device flow.
        </p>
      }
    >
      <form
        ref={formRef}
        onSubmit={onSubmit}
        className="flex flex-col gap-5 w-full"
        noValidate
      >
        <div className="flex flex-col gap-2">
          <label htmlFor="device-user-code" className="oidc-sci-fi-label">
            Verification Code
          </label>
          <input
            id="device-user-code"
            type="text"
            inputMode="text"
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            placeholder="ABCD-EFGH"
            value={value}
            onChange={onChange}
            maxLength={9}
            disabled={isAuthenticating}
            aria-invalid={!!serverError}
            className="oidc-sci-fi-input"
            style={{ letterSpacing: "0.18em", textTransform: "uppercase" }}
          />
        </div>

        <div className="flex items-center gap-2">
          <span className="oidc-sci-fi-label" style={{ marginBottom: 0 }}>
            Tenant
          </span>
          <span className="oidc-sci-fi-badge">{tenantId}</span>
        </div>

        {serverError && (
          <p
            aria-live="polite"
            className="text-sm"
            style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}
          >
            {serverError}
          </p>
        )}

        <button
          type="submit"
          disabled={isAuthenticating || !value}
          className="oidc-sci-fi-btn mt-3 w-full flex items-center justify-center gap-2"
        >
          {isAuthenticating ? (
            <>
              <Loader size={16} className="oidc-spin-slow" />
              <span>Resolving…</span>
            </>
          ) : (
            <span>Continue</span>
          )}
        </button>

        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          Not the right tenant?{" "}
          <Link to={buildOIDCNavigationUrl("/oidc/login")} className="oidc-sci-fi-link">
            Back to sign-in
          </Link>
        </p>
      </form>
    </OidcAuthShell>
  );
}
