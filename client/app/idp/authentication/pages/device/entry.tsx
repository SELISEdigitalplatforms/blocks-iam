import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { Loader } from "lucide-react";

import { OidcAuthShell, OidcFooter, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { DEVICE_ENTRY_PANEL, DEVICE_CONSENT_PANEL } from "./panel-config";
import {
  formatUserCodeForDisplay,
  isValidUserCode,
  normalizeUserCode,
} from "@blocks-idp/authentication/utils/device-utils";
import {
  deviceService,
  type DeviceConsentPayload,
} from "@blocks-idp/authentication/services/device.service";

type FlowState =
  | { kind: "idle" }
  | { kind: "login_required"; returnUrl: string }
  | { kind: "ready"; payload: DeviceConsentPayload }
  | { kind: "expired" }
  | { kind: "tenant_mismatch"; payload: DeviceConsentPayload }
  | { kind: "error"; message: string };

export function DeviceEntryPage() {
  const params = useParams<{ tenantId: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const tenantId = (params.tenantId ?? "").trim();
  const animCtx = useOidcAuthAnimation();
  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template;

  const initialCode = useMemo(() => {
    const fromQuery = searchParams.get("user_code") ?? "";
    return fromQuery ? formatUserCodeForDisplay(fromQuery) : "";
  }, [searchParams]);

  const [value, setValue] = useState(initialCode);
  const [flow, setFlow] = useState<FlowState>({ kind: "idle" });
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [decision, setDecision] = useState<null | "allow" | "deny">(null);
  const formRef = useRef<HTMLFormElement>(null);
  const consentRef = useRef<HTMLDivElement>(null);
  const autoSubmittedRef = useRef(false);

  const invalidTenant = !tenantId;

  // The device page's own URL never carries client_id/scope (only user_code, via a
  // path param tenantId) — buildOIDCNavigationUrl() would read those off this page's
  // params and get nothing meaningful. tenantId is the only real context this page
  // has pre-verification, since a device code hasn't been resolved to a client yet.
  const backToSignInUrl = tenantId
    ? `/oidc/login?tenant_id=${encodeURIComponent(tenantId)}`
    : "/oidc/login";

  function shake(el: HTMLElement | null) {
    if (!el) return;
    el.classList.remove("oidc-animate-shake");
    void el.offsetWidth;
    el.classList.add("oidc-animate-shake");
  }

  useEffect(() => {
    if (invalidTenant || !initialCode || autoSubmittedRef.current) return;
    if (!isValidUserCode(initialCode)) return;
    autoSubmittedRef.current = true;
    void runSubmit(normalizeUserCode(initialCode), true);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [initialCode, invalidTenant]);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-[var(--bg)]">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  const shellTemplateProps = {
    theme: template.theme,
    logoUrl: template.branding.logoUrl,
    brandName: template.branding.brandName,
    successTitle: "Device Verified",
    successSubtitle: "Continuing the device flow…",
    footerNote: <OidcFooter footerText={template.pages.shared.footerText} />,
  };

  async function runSubmit(code: string, isAutoSubmit = false) {
    setServerError(null);
    setIsSubmitting(true);
    animCtx?.startAnimation();
    try {
      const res = await deviceService.verify(code, tenantId);
      await handleVerifyResult(res, code);
    } catch (err: unknown) {
      shake(formRef.current);
      const errCode = readErrorCode(err);
      const msg = verifyErrorMessage(errCode);
      if (errCode === "expired_token" && isAutoSubmit) {
        // A code auto-submitted from the URL (refresh, back button, bookmark) is
        // dead by definition of the user never having typed it — drop to a clean
        // entry form instead of the terminal "expired" screen, and strip user_code
        // so a future refresh of this page doesn't just re-trigger the same dead end.
        setSearchParams((prev) => {
          const next = new URLSearchParams(prev);
          next.delete("user_code");
          return next;
        });
        setValue("");
        setServerError("That code has expired. Enter the fresh code shown on your device.");
      } else if (errCode === "expired_token") {
        setFlow({ kind: "expired" });
      } else {
        setServerError(msg);
      }
      await animCtx?.failAnimation(msg);
      setIsSubmitting(false);
    }
  }

  async function handleVerifyResult(
    res: { status: string; payload?: DeviceConsentPayload; returnUrl?: string },
    code: string,
  ) {
    if (res.status === "login_required") {
      const url = res.returnUrl ?? `/oidc/login?returnUrl=${encodeURIComponent(window.location.href)}`;
      window.location.assign(url);
      return;
    }

    if (res.status === "ready" && res.payload) {
      if (res.payload.tenant && res.payload.tenant !== tenantId) {
        setFlow({ kind: "tenant_mismatch", payload: res.payload });
        await animCtx?.failAnimation("Tenant mismatch");
        setIsSubmitting(false);
        return;
      }
      setFlow({ kind: "ready", payload: res.payload });
      await animCtx?.succeedAnimation();
      setIsSubmitting(false);
      return;
    }

    shake(formRef.current);
    setServerError("Unexpected response from server.");
    await animCtx?.failAnimation("Unexpected response");
    setIsSubmitting(false);
    void code;
  }

  async function submitDecision(choice: "allow" | "deny") {
    if (flow.kind !== "ready" || decision || isSubmitting) return;
    setServerError(null);
    setDecision(choice);
    animCtx?.startAnimation();
    try {
      const res = await deviceService.decide(
        flow.payload.userCode,
        choice,
        tenantId,
        flow.payload.approvalToken,
      );
      await animCtx?.succeedAnimation();
      window.location.assign(res.redirect);
    } catch (err: unknown) {
      shake(consentRef.current);
      const code = readErrorCode(err);
      if (code === "request_not_pending" || code === "expired_token") {
        setFlow({ kind: "expired" });
      } else if (code === "login_required") {
        setServerError("Your sign-in session expired. Sign in again and retry this code.");
      } else if (code === "slow_down") {
        setServerError("Too many attempts. Wait a moment, then try again.");
      } else {
        setServerError("We could not record your decision. Please try again.");
      }
      await animCtx?.failAnimation("Decision failed");
      setDecision(null);
    }
  }

  function readErrorCode(err: unknown): string | undefined {
    return (
      (err as { errors?: { error?: string } })?.errors?.error ??
      (err as { error?: string })?.error
    );
  }

  function verifyErrorMessage(code?: string): string {
    if (code === "expired_token") return "This device code has expired.";
    if (code === "slow_down") return "Too many attempts. Wait a moment, then try again.";
    if (code === "invalid_grant") return "Invalid code, wrong tenant, or already used.";
    return "Invalid or expired code.";
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || invalidTenant) return;
    const code = normalizeUserCode(value);
    if (!isValidUserCode(code)) {
      shake(formRef.current);
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
        {...shellTemplateProps}
        heading="Device Verification"
        headingDimFirst={2}
        showCorners={false}
        footerNote={
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            <Link to={backToSignInUrl} className="oidc-sci-fi-link">
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

  if (flow.kind === "login_required") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_ENTRY_PANEL}
        {...shellTemplateProps}
        heading="Please sign in"
        headingDimFirst={2}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            You need to authenticate before you can authorize this device.
          </p>
          <button
            type="button"
            onClick={() => window.location.assign(flow.returnUrl)}
            className="oidc-sci-fi-btn w-full"
          >
            Sign in
          </button>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "expired") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_ENTRY_PANEL}
        {...shellTemplateProps}
        heading="Device code expired"
        headingDimFirst={3}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            This device code has expired or has already been used.
          </p>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
            Start over
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "tenant_mismatch") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
        {...shellTemplateProps}
        heading="Tenant mismatch"
        headingDimFirst={2}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <div
            className="rounded-lg p-4"
            style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
          >
            <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
              Wrong tenant
            </p>
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              This interaction belongs to <strong>{flow.payload.tenant}</strong>, not <strong>{tenantId}</strong>.
            </p>
          </div>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
            Use another code
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "ready") {
    const payload = flow.payload;
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
        {...shellTemplateProps}
        heading="Authorize device"
        headingDimFirst={2}
        showCorners={false}
      >
        <div ref={consentRef} className="flex flex-col gap-4 w-full">
          <div className="oidc-sci-fi-panel-border">
            <div className="oidc-sci-fi-panel-inner p-5 sm:p-6 space-y-4">
              <h2 className="text-lg oidc-font-orbitron" style={{ color: "var(--fg)" }}>
                Authorize this device?
              </h2>

              <div className="flex flex-wrap gap-2">
                <span className="oidc-sci-fi-badge">{payload.clientName || payload.clientId}</span>
                <span className="oidc-sci-fi-badge">{payload.userCode}</span>
              </div>

              <div className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
                <p style={{ color: "var(--fg)" }} className="font-semibold mb-2">
                  This device would like to:
                </p>
                {payload.scopes.length > 0 ? (
                  <ul className="list-disc list-inside space-y-1 pl-4">
                    {payload.scopes.map((scope) => (
                      <li key={scope}>{scopeDescription(scope)}</li>
                    ))}
                  </ul>
                ) : (
                  <p>Access your account.</p>
                )}
              </div>

              {(payload.requestIpAddress || payload.requestUserAgent || payload.deviceName || payload.deviceInfo) && (
                <div
                  className="rounded-md p-3 text-xs oidc-font-rajdhani"
                  style={{ background: "var(--panel)", border: "1px solid var(--border)", color: "var(--muted)" }}
                >
                  {payload.deviceName && (
                    <p>
                      Device: <span style={{ color: "var(--fg)" }}>{payload.deviceName}</span>
                    </p>
                  )}
                  {payload.requestIpAddress && (
                    <p>
                      Request IP: <span style={{ color: "var(--fg)" }}>{payload.requestIpAddress}</span>
                    </p>
                  )}
                  {payload.requestUserAgent && (
                    <p className="break-words">
                      Browser: <span style={{ color: "var(--fg)" }}>{payload.requestUserAgent}</span>
                    </p>
                  )}
                  {payload.deviceInfo && (
                    <p className="break-words">
                      Details: <span style={{ color: "var(--fg)" }}>{payload.deviceInfo}</span>
                    </p>
                  )}
                </div>
              )}

              {serverError && (
                <p
                  aria-live="polite"
                  className="text-sm"
                  style={{ color: "var(--danger)", fontFamily: "system-ui, sans-serif" }}
                >
                  {serverError}
                </p>
              )}

              <div className="flex flex-col sm:flex-row gap-3 pt-2">
                <button
                  type="button"
                  disabled={!!decision}
                  onClick={() => submitDecision("deny")}
                  className="oidc-sci-fi-btn-outline flex-1"
                >
                  {decision === "deny" ? (
                    <span className="inline-flex items-center gap-2 justify-center">
                      <Loader size={14} className="oidc-spin-slow" />
                      Denying…
                    </span>
                  ) : (
                    "Deny"
                  )}
                </button>
                <button
                  type="button"
                  disabled={!!decision}
                  onClick={() => submitDecision("allow")}
                  className="oidc-sci-fi-btn flex-1"
                >
                  {decision === "allow" ? (
                    <span className="inline-flex items-center gap-2 justify-center">
                      <Loader size={14} className="oidc-spin-slow" />
                      Authorizing…
                    </span>
                  ) : (
                    "Allow"
                  )}
                </button>
              </div>

              <Link
                to={`/device/${tenantId}`}
                className="oidc-sci-fi-link text-center block text-xs"
                onClick={() => setFlow({ kind: "idle" })}
              >
                Cancel
              </Link>
            </div>
          </div>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "error") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_ENTRY_PANEL}
        {...shellTemplateProps}
        heading="Device Verification"
        headingDimFirst={2}
        showCorners={false}
      >
        <div
          className="rounded-lg p-4"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
            Error
          </p>
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
            {flow.message}
          </p>
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
      {...shellTemplateProps}
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
          <Link to={backToSignInUrl} className="oidc-sci-fi-link">
            Back to sign-in
          </Link>
        </p>
      </form>
    </OidcAuthShell>
  );
}

function scopeDescription(scope: string): string {
  const map: Record<string, string> = {
    openid: "Authenticate you with your Blocks account",
    profile: "Access your basic profile information",
    email: "Access your email address",
    offline_access: "Stay signed in when you are offline",
  };
  return map[scope] ?? scope;
}
