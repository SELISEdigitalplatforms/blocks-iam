import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Loader } from "lucide-react";

import { OidcAuthShell, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { DEVICE_CONSENT_PANEL } from "./panel-config";
import {
  deviceService,
  type DeviceConsentPayload,
} from "@blocks-idp/authentication/services/device.service";

type LoadState =
  | { kind: "loading" }
  | { kind: "ready"; payload: DeviceConsentPayload }
  | { kind: "login_required" }
  | { kind: "expired" }
  | { kind: "tenant_mismatch"; payload: DeviceConsentPayload }
  | { kind: "error"; message: string };

export function DeviceContinuePage() {
  const params = useParams<{ tenantId: string; interactionId: string }>();
  const tenantId = (params.tenantId ?? "").trim();
  const interactionId = (params.interactionId ?? "").trim();
  const animCtx = useOidcAuthAnimation();

  const [state, setState] = useState<LoadState>({ kind: "loading" });
  const [serverError, setServerError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState<null | "allow" | "deny">(null);
  const formRef = useRef<HTMLDivElement>(null);

  const invalid = !tenantId || !interactionId;

  function shake() {
    if (!formRef.current) return;
    formRef.current.classList.remove("oidc-animate-shake");
    void formRef.current.offsetWidth;
    formRef.current.classList.add("oidc-animate-shake");
  }

  useEffect(() => {
    if (invalid) return;
    let cancelled = false;
    (async () => {
      animCtx?.startAnimation();
      try {
        const payload = await deviceService.loadConsent(interactionId, tenantId);
        if (cancelled) return;
        if (payload.tenant && payload.tenant !== tenantId) {
          setState({ kind: "tenant_mismatch", payload });
          await animCtx?.failAnimation("Tenant mismatch");
          return;
        }
        setState({ kind: "ready", payload });
        await animCtx?.succeedAnimation();
      } catch (err: unknown) {
        if (cancelled) return;
        const status = (err as { status?: number; errors?: { error?: string } })?.status;
        const code = (err as { errors?: { error?: string } })?.errors?.error;
        if (status === 401 || code === "login_required") {
          setState({ kind: "login_required" });
          await animCtx?.failAnimation("Please sign in");
          return;
        }
        if (status === 410 || code === "interaction_expired" || code === "request_not_pending") {
          setState({ kind: "expired" });
          await animCtx?.failAnimation("Code expired");
          return;
        }
        const msg = "We could not load this device request.";
        setState({ kind: "error", message: msg });
        await animCtx?.failAnimation(msg);
      }
    })();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tenantId, interactionId]);

  async function submitDecision(decision: "allow" | "deny") {
    if (!interactionId || !tenantId || isSubmitting) return;
    setServerError(null);
    setIsSubmitting(decision);
    animCtx?.startAnimation();
    try {
      const res = await deviceService.approve(interactionId, decision, tenantId);
      await animCtx?.succeedAnimation();
      window.location.assign(res.redirect);
    } catch (err: unknown) {
      shake();
      const status = (err as { status?: number })?.status;
      const code = (err as { errors?: { error?: string } })?.errors?.error;
      if (status === 410 || code === "interaction_expired" || code === "request_not_pending") {
        setState({ kind: "expired" });
      }
      const msg =
        status === 410 || code === "interaction_expired" || code === "request_not_pending"
          ? "This device code is no longer valid."
          : "We could not record your decision. Please try again.";
      setServerError(msg);
      await animCtx?.failAnimation(msg);
      setIsSubmitting(null);
    }
  }

  if (invalid) {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
        heading="Device Consent"
        headingDimFirst={2}
        showCorners={false}
      >
        <div
          className="rounded-lg p-4"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
            Invalid Link
          </p>
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
            This URL is missing the tenant or interaction identifier.
          </p>
        </div>
      </OidcAuthShell>
    );
  }

  if (state.kind === "loading") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
        heading="Authorizing device"
        headingDimFirst={2}
        showCorners={false}
      >
        <div className="flex items-center justify-center py-6">
          <Loader size={28} className="oidc-spin-slow" style={{ color: "var(--accent2)" }} />
        </div>
      </OidcAuthShell>
    );
  }

  if (state.kind === "login_required") {
    const returnUrl = `/device/${tenantId}/continue/${interactionId}`;
    const loginUrl = `/oidc/login?tenantId=${encodeURIComponent(tenantId)}&returnUrl=${encodeURIComponent(returnUrl)}`;
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
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
            onClick={() => window.location.assign(loginUrl)}
            className="oidc-sci-fi-btn w-full"
          >
            Sign in
          </button>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-link text-center">
            Cancel
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (state.kind === "expired") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
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

  if (state.kind === "tenant_mismatch") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
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
              This interaction belongs to <strong>{state.payload.tenant}</strong>, not <strong>{tenantId}</strong>.
            </p>
          </div>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
            Use another code
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (state.kind === "error") {
    return (
      <OidcAuthShell
        panelConfig={DEVICE_CONSENT_PANEL}
        heading="Device Consent"
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
            {state.message}
          </p>
        </div>
      </OidcAuthShell>
    );
  }

  return (
    <OidcAuthShell
      panelConfig={DEVICE_CONSENT_PANEL}
      heading="Authorize device"
      headingDimFirst={2}
      showCorners={false}
    >
      <div ref={formRef} className="flex flex-col gap-4 w-full">
        <div className="oidc-sci-fi-panel-border">
          <div className="oidc-sci-fi-panel-inner p-5 sm:p-6 space-y-4">
            <h2 className="text-lg oidc-font-orbitron" style={{ color: "var(--fg)" }}>
              Authorize this device?
            </h2>

            <div className="flex flex-wrap gap-2">
              <span className="oidc-sci-fi-badge">{state.payload.clientName || state.payload.clientId}</span>
              <span className="oidc-sci-fi-badge">{state.payload.userCode}</span>
            </div>

            <div className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
              <p style={{ color: "var(--fg)" }} className="font-semibold mb-2">
                This device would like to:
              </p>
              {state.payload.scopes.length > 0 ? (
                <ul className="list-disc list-inside space-y-1 pl-4">
                  {state.payload.scopes.map((scope) => (
                    <li key={scope}>{scopeDescription(scope)}</li>
                  ))}
                </ul>
              ) : (
                <p>Access your account.</p>
              )}
            </div>

            <div className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
              Tenant: <span style={{ color: "var(--fg)" }}>{state.payload.tenant || tenantId}</span>
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

            <div className="flex flex-col sm:flex-row gap-3 pt-2">
              <button
                type="button"
                disabled={!!isSubmitting}
                onClick={() => submitDecision("deny")}
                className="oidc-sci-fi-btn-outline flex-1"
              >
                {isSubmitting === "deny" ? (
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
                disabled={!!isSubmitting}
                onClick={() => submitDecision("allow")}
                className="oidc-sci-fi-btn flex-1"
              >
                {isSubmitting === "allow" ? (
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
            >
              Cancel
            </Link>
          </div>
        </div>
      </div>
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
