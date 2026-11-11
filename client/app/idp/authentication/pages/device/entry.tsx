import { useEffect, useMemo, useRef, useState } from "react";
import { Link, useParams, useSearchParams } from "react-router";
import { Loader } from "lucide-react";

import { OidcAuthShell, OidcFooter, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import { useT, type TFunction } from "@seliseblocks/genesis-os/i18n";
import { buildDeviceConsentPanel, buildDeviceEntryPanel } from "./panel-config";
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

function readErrorCode(err: unknown): string | undefined {
  return (
    (err as { errors?: { error?: string } })?.errors?.error ??
    (err as { error?: string })?.error
  );
}

function verifyErrorMessage(t: TFunction, code?: string): string {
  if (code === "expired_token") return t("DEVICE_ENTRY.ERROR_EXPIRED_TOKEN");
  if (code === "slow_down") return t("DEVICE_ENTRY.ERROR_SLOW_DOWN");
  if (code === "invalid_grant") return t("DEVICE_ENTRY.ERROR_INVALID_GRANT");
  return t("DEVICE_ENTRY.ERROR_INVALID_OR_EXPIRED");
}

/** Where the sign-in request came from, as far as the device told us; nothing when it told us nothing. */
function DeviceRequestDetails({ payload }: Readonly<{ payload: DeviceConsentPayload }>) {
  const { t } = useT("iam.device");
  if (!(payload.requestIpAddress || payload.requestUserAgent || payload.deviceName || payload.deviceInfo)) return null;
  return (
    <div
      className="rounded-md p-3 text-xs oidc-font-rajdhani"
      style={{ background: "var(--panel)", border: "1px solid var(--border)", color: "var(--muted)" }}
    >
      {payload.deviceName && (
        <p>
          {t("DEVICE_ENTRY.DETAIL_DEVICE")} <span style={{ color: "var(--fg)" }}>{payload.deviceName}</span>
        </p>
      )}
      {payload.requestIpAddress && (
        <p>
          {t("DEVICE_ENTRY.DETAIL_REQUEST_IP")} <span style={{ color: "var(--fg)" }}>{payload.requestIpAddress}</span>
        </p>
      )}
      {payload.requestUserAgent && (
        <p className="wrap-break-word">
          {t("DEVICE_ENTRY.DETAIL_BROWSER")} <span style={{ color: "var(--fg)" }}>{payload.requestUserAgent}</span>
        </p>
      )}
      {payload.deviceInfo && (
        <p className="wrap-break-word">
          {t("DEVICE_ENTRY.DETAIL_DETAILS")} <span style={{ color: "var(--fg)" }}>{payload.deviceInfo}</span>
        </p>
      )}
    </div>
  );
}

export function DeviceEntryPage() {
  const { t } = useT("iam.device");
  const entryPanel = useMemo(() => buildDeviceEntryPanel(t), [t]);
  const consentPanel = useMemo(() => buildDeviceConsentPanel(t), [t]);
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
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  const shellTemplateProps = {
    showAnimationPanel: template.layout.showAnimationPanel,
    theme: template.theme,
    logoUrlLight: template.branding.logoUrlLight,
    logoUrlDark: template.branding.logoUrlDark,
    brandName: template.branding.brandName,
    successTitle: t("DEVICE_ENTRY.VERIFIED_TITLE"),
    successSubtitle: t("DEVICE_ENTRY.VERIFIED_SUBTITLE"),
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
      const msg = verifyErrorMessage(t, errCode);
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
        setServerError(t("DEVICE_ENTRY.CODE_EXPIRED_ENTER_FRESH"));
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
        await animCtx?.failAnimation(t("DEVICE_ENTRY.TENANT_MISMATCH"));
        setIsSubmitting(false);
        return;
      }
      setFlow({ kind: "ready", payload: res.payload });
      await animCtx?.succeedAnimation();
      setIsSubmitting(false);
      return;
    }

    shake(formRef.current);
    setServerError(t("DEVICE_ENTRY.UNEXPECTED_RESPONSE_SERVER"));
    await animCtx?.failAnimation(t("DEVICE_ENTRY.UNEXPECTED_RESPONSE"));
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
        setServerError(t("DEVICE_ENTRY.SESSION_EXPIRED"));
      } else if (code === "slow_down") {
        setServerError(t("DEVICE_ENTRY.ERROR_SLOW_DOWN"));
      } else if (code === "organization_disabled") {
        // The request stays pending on the server; only the message changes.
        setServerError(t("iam.shared:ORGANIZATION.DISABLED_MESSAGE"));
      } else {
        setServerError(t("DEVICE_ENTRY.DECISION_NOT_RECORDED"));
      }
      await animCtx?.failAnimation(t("DEVICE_ENTRY.DECISION_FAILED"));
      setDecision(null);
    }
  }

  function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (isSubmitting || invalidTenant) return;
    const code = normalizeUserCode(value);
    if (!isValidUserCode(code)) {
      shake(formRef.current);
      const msg = t("DEVICE_ENTRY.ENTER_CODE_HINT");
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
        panelConfig={entryPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.DEVICE_VERIFICATION")}
        headingDimFirst={2}
        showCorners={false}
        footerNote={
          <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            <Link to={backToSignInUrl} className="oidc-sci-fi-link">
              {t("DEVICE_ENTRY.BACK_TO_SIGN_IN")}
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
              {t("DEVICE_ENTRY.MISSING_TENANT")}
            </p>
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              {t("DEVICE_ENTRY.MISSING_TENANT_DETAIL")}
            </p>
          </div>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "login_required") {
    return (
      <OidcAuthShell
        panelConfig={entryPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.PLEASE_SIGN_IN")}
        headingDimFirst={2}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            {t("DEVICE_ENTRY.AUTHENTICATE_FIRST")}
          </p>
          <button
            type="button"
            onClick={() => window.location.assign(flow.returnUrl)}
            className="oidc-sci-fi-btn w-full"
          >
            {t("DEVICE_ENTRY.SIGN_IN")}
          </button>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "expired") {
    return (
      <OidcAuthShell
        panelConfig={entryPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.CODE_EXPIRED_HEADING")}
        headingDimFirst={3}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
            {t("DEVICE_ENTRY.CODE_EXPIRED_OR_USED")}
          </p>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
            {t("DEVICE_ENTRY.START_OVER")}
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "tenant_mismatch") {
    return (
      <OidcAuthShell
        panelConfig={consentPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.TENANT_MISMATCH")}
        headingDimFirst={2}
        showCorners={false}
      >
        <div className="flex flex-col gap-4">
          <div
            className="rounded-lg p-4"
            style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
          >
            <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
              {t("DEVICE_ENTRY.WRONG_TENANT")}
            </p>
            <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--danger)" }}>
              {t("DEVICE_ENTRY.WRONG_TENANT_DETAIL", { tenant: flow.payload.tenant, expected: tenantId })}
            </p>
          </div>
          <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
            {t("DEVICE_ENTRY.USE_ANOTHER_CODE")}
          </Link>
        </div>
      </OidcAuthShell>
    );
  }

  if (flow.kind === "ready") {
    const payload = flow.payload;
    return (
      <OidcAuthShell
        panelConfig={consentPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.AUTHORIZE_DEVICE")}
        headingDimFirst={2}
        showCorners={false}
      >
        <div ref={consentRef} className="flex flex-col gap-4 w-full">
          <div className="oidc-sci-fi-panel-border">
            <div className="oidc-sci-fi-panel-inner p-5 sm:p-6 space-y-4">
              <h2 className="text-lg oidc-font-orbitron" style={{ color: "var(--fg)" }}>
                {t("DEVICE_ENTRY.AUTHORIZE_THIS_DEVICE")}
              </h2>

              <div className="flex flex-wrap gap-2">
                <span className="oidc-sci-fi-badge">{payload.clientName || payload.clientId}</span>
                <span className="oidc-sci-fi-badge">{payload.userCode}</span>
              </div>

              <div className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
                <p style={{ color: "var(--fg)" }} className="font-semibold mb-2">
                  {t("DEVICE_ENTRY.DEVICE_WOULD_LIKE_TO")}
                </p>
                {payload.scopes.length > 0 ? (
                  <ul className="list-disc list-inside space-y-1 pl-4">
                    {payload.scopes.map((scope) => (
                      <li key={scope}>{scopeDescription(t, scope)}</li>
                    ))}
                  </ul>
                ) : (
                  <p>{t("DEVICE_ENTRY.ACCESS_YOUR_ACCOUNT")}</p>
                )}
              </div>

              <DeviceRequestDetails payload={payload} />

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
                      {t("DEVICE_ENTRY.DENYING")}
                    </span>
                  ) : (
                    t("DEVICE_ENTRY.DENY")
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
                      {t("DEVICE_ENTRY.AUTHORIZING")}
                    </span>
                  ) : (
                    t("DEVICE_ENTRY.ALLOW")
                  )}
                </button>
              </div>

              <Link
                to={`/device/${tenantId}`}
                className="oidc-sci-fi-link text-center block text-xs"
                onClick={() => setFlow({ kind: "idle" })}
              >
                {t("common:CANCEL")}
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
        panelConfig={entryPanel}
        {...shellTemplateProps}
        heading={t("DEVICE_ENTRY.DEVICE_VERIFICATION")}
        headingDimFirst={2}
        showCorners={false}
      >
        <div
          className="rounded-lg p-4"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <p className="mb-1 text-sm font-semibold oidc-font-orbitron" style={{ color: "var(--danger)" }}>
            {t("common:ERROR")}
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
      panelConfig={entryPanel}
      {...shellTemplateProps}
      heading={t("DEVICE_ENTRY.ENTER_YOUR_DEVICE_CODE")}
      headingDimFirst={3}
      showCorners={false}
      footerNote={
        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          {t("DEVICE_ENTRY.FOOTER", { year: new Date().getFullYear() })}
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
            {t("DEVICE_ENTRY.VERIFICATION_CODE")}
          </label>
          <input
            id="device-user-code"
            type="text"
            inputMode="text"
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            placeholder={t("DEVICE_ENTRY.CODE_PLACEHOLDER")}
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
              <span>{t("DEVICE_ENTRY.RESOLVING")}</span>
            </>
          ) : (
            <span>{t("common:CONTINUE")}</span>
          )}
        </button>

        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          {t("DEVICE_ENTRY.NOT_RIGHT_TENANT")}{" "}
          <Link to={backToSignInUrl} className="oidc-sci-fi-link">
            {t("DEVICE_ENTRY.BACK_TO_SIGN_IN")}
          </Link>
        </p>
      </form>
    </OidcAuthShell>
  );
}

function scopeDescription(t: TFunction, scope: string): string {
  const map: Record<string, string> = {
    openid: t("DEVICE_ENTRY.SCOPE_OPENID"),
    profile: t("DEVICE_ENTRY.SCOPE_PROFILE"),
    email: t("DEVICE_ENTRY.SCOPE_EMAIL"),
    offline_access: t("DEVICE_ENTRY.SCOPE_OFFLINE_ACCESS"),
  };
  return map[scope] ?? scope;
}
