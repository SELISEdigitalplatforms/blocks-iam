import { useEffect, useRef, useState } from "react";
import { useParams } from "react-router";
import { tStatic, useT } from "@seliseblocks/genesis-os/i18n";
import { Loader } from "lucide-react";
import { showErrorToast } from "@/hooks/use-toast";
import { useOidcUiConfig } from "@blocks-idp/authentication/hooks/use-oidc-ui-config";
import {
  useCompleteSignupLinkMfa,
  useRedeemSignupLink,
  useSignupLinkContext,
} from "@blocks-idp/authentication/hooks/use-signup-link";
import { OidcAuthShell, OidcFooter } from "../oidc/oidc-auth-shell";
import { JOIN_PANEL } from "../oidc/oidc-panel-config";
import { InvitationInvalid } from "./invitation-invalid";
import { InvitationLoading } from "./invitation-loading";
import {
  CONFIRM_PASSWORD_COPY,
  InvitationConfirmPassword,
  type ConfirmPasswordExit,
} from "./invitation-confirm-password";
import { LoginReturnLink } from "@blocks-idp/authentication/components/login-return-link";
import { Button } from "@/components/ui-kits/button/button";
import { isErrorWithErrors } from "@/lib/error";
import type { RedeemSignupLinkResponse } from "@blocks-idp/authentication/services/signup-link.service";
import type { IAccountActivationResponse } from "@blocks-idp/iam/models/user";
import { Activation } from "../activation";

function readLinkCode(): string | null {
  if (globalThis.window === undefined) return null;
  const { location } = globalThis.window;
  const hash = location.hash.startsWith("#") ? location.hash.slice(1) : location.hash;
  const params = new URLSearchParams(hash);
  const raw = params.get("link") ?? (hash.startsWith("link=") ? hash.slice(5) : "");
  return raw || null;
}

function clearLinkHash() {
  if (globalThis.window === undefined) return;
  const { location, history } = globalThis.window;
  if (location.hash) {
    history.replaceState(null, "", `${location.pathname}${location.search}`);
  }
}

export const getConfirmStepTimedOut = () => tStatic("iam.invitation", "INVITATION.STEP_TIMED_OUT");
export const EMBEDDED_LINK_COPY = {
  get heading() {
    return tStatic("iam.invitation", "INVITATION.EMBEDDED_HEADING");
  },
  get message() {
    return tStatic("iam.invitation", "INVITATION.EMBEDDED_MESSAGE");
  },
};
export const getNoCredentialYetCopy = () => tStatic("iam.invitation", "INVITATION.NO_PASSWORD_YET");

type PasswordStep = { redemptionId: string; maskedEmail: string };

function applyRedeemNavigation(res: RedeemSignupLinkResponse | undefined): {
  mfaId?: string;
  userMfa?: string | null;
  activationKey?: string;
  passwordStep?: PasswordStep;
} {
  if (!res) return {};
  if (res.error === "authentication_required" && res.redemptionId) {
    return {
      passwordStep: { redemptionId: res.redemptionId, maskedEmail: res.maskedEmail ?? "" },
    };
  }
  if (res.authorizeUrl) {
    globalThis.window.location.assign(res.authorizeUrl);
    return {};
  }
  if (res.loginUrl) {
    globalThis.window.location.assign(res.loginUrl);
    return {};
  }
  if (res.mfaId) {
    return { mfaId: res.mfaId, userMfa: res.userMfa ?? null };
  }
  if (res.activationKey) {
    return { activationKey: res.activationKey };
  }
  return {};
}

/**
 * A Blocks OIDC link's activation answers with the authorize request back into the app, signed
 * in or through its sign-in screen as the configuration chose. Without one, the activation
 * success page follows as for any other invitation.
 */
function followAuthorizeUrl(res: IAccountActivationResponse): boolean {
  if (!res.authorizeUrl) return false;
  globalThis.window.location.assign(res.authorizeUrl);
  return true;
}

export function InvitationPage() {
  const { t } = useT("iam.invitation");
  const { tenantId } = useParams<{ tenantId: string }>();
  // The link code is read from the URL hash once, on mount; the hash is then cleared so the
  // code never lingers in the address bar or history.
  const [code] = useState(readLinkCode);
  const redeemedRef = useRef(false);
  const [mfaId, setMfaId] = useState<string | null>(null);
  const [mfaCode, setMfaCode] = useState("");
  const [activationKey, setActivationKey] = useState<string | null>(null);
  const [userMfa, setUserMfa] = useState<string | null>(null);
  const [passwordStep, setPasswordStep] = useState<PasswordStep | null>(null);
  const [passwordNotSet, setPasswordNotSet] = useState(false);
  const [passwordExit, setPasswordExit] = useState<ConfirmPasswordExit | null>(null);

  useEffect(() => {
    clearLinkHash();
  }, []);

  const { data: oidcUiConfig } = useOidcUiConfig(tenantId);
  const template = oidcUiConfig?.template ?? null;
  const contextQuery = useSignupLinkContext(code, tenantId);
  const redeemMutation = useRedeemSignupLink(tenantId);
  const mfaMutation = useCompleteSignupLinkMfa(tenantId);

  const onRedeemResult = (res: RedeemSignupLinkResponse | undefined) => {
    const next = applyRedeemNavigation(res);
    if (next.passwordStep) {
      setPasswordStep(next.passwordStep);
      return;
    }
    if (next.mfaId) {
      setPasswordStep(null);
      setMfaId(next.mfaId);
      setUserMfa(next.userMfa ?? null);
    }
    if (next.activationKey) setActivationKey(next.activationKey);
  };

  useEffect(() => {
    if (!contextQuery.data?.valid || !code) return;
    // Redeeming would spend the code and answer with tokens this page cannot use; the context
    // lookup spends nothing, so the app's own join page can still redeem it.
    if (contextQuery.data.mode === "Embedded") return;
    if (redeemedRef.current || redeemMutation.isPending || redeemMutation.isSuccess) return;
    redeemedRef.current = true;
    redeemMutation.mutate(code, {
      onSuccess: onRedeemResult,
      onError: (error: unknown) => {
        if (isErrorWithErrors(error) && error.errors?.error === "password_not_set") {
          setPasswordNotSet(true);
          return;
        }
        showErrorToast({ errors: t("INVITATION.SOMETHING_WENT_WRONG") });
        redeemedRef.current = false;
      },
    });
  }, [contextQuery.data, code, redeemMutation, t]);

  if (!template) {
    return (
      <div className="oidc-scifi-root min-h-screen flex items-center justify-center bg-(--bg)">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
      </div>
    );
  }

  if (contextQuery.isLoading || contextQuery.isFetching) {
    return <InvitationLoading template={template} />;
  }

  if (passwordExit === "invalid_redemption") {
    return <InvitationInvalid template={template} message={getConfirmStepTimedOut()} />;
  }

  if (passwordExit === "invalid_link") {
    return <InvitationInvalid template={template} />;
  }

  if (!code || contextQuery.isError || !contextQuery.data?.valid) {
    return <InvitationInvalid template={template} />;
  }

  const shellProps = {
    panelConfig: JOIN_PANEL,
    theme: template.theme,
    logoUrlLight: template.branding.logoUrlLight,
    logoUrlDark: template.branding.logoUrlDark,
    brandName: template.branding.brandName,
    headingDimFirst: 0 as const,
    headingAlign: "left" as const,
    successTitle: t("INVITATION.YOU_RE_IN"),
    successSubtitle: "",
    showCorners: false,
    footerNote: <OidcFooter footerText={template.pages.shared.footerText} />,
  };

  if (contextQuery.data.mode === "Embedded") {
    return (
      <OidcAuthShell {...shellProps} heading={EMBEDDED_LINK_COPY.heading}>
        <p className="py-6 text-center text-sm" style={{ color: "var(--fg)" }}>
          {EMBEDDED_LINK_COPY.message}
        </p>
      </OidcAuthShell>
    );
  }

  if (passwordNotSet) {
    return (
      <OidcAuthShell {...shellProps} heading={t("INVITATION.SIGN_IN_FIRST")}>
        <div className="flex flex-col items-center gap-4 py-6 text-center">
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            {getNoCredentialYetCopy()}
          </p>
          <Button asChild variant="outline">
            <LoginReturnLink preferOidcLogin>{t("INVITATION.SIGN_IN")}</LoginReturnLink>
          </Button>
        </div>
      </OidcAuthShell>
    );
  }

  if (passwordStep && !mfaId) {
    return (
      <OidcAuthShell {...shellProps} heading={CONFIRM_PASSWORD_COPY.heading}>
        <InvitationConfirmPassword
          tenantId={tenantId}
          redemptionId={passwordStep.redemptionId}
          maskedEmail={passwordStep.maskedEmail}
          onSuccess={onRedeemResult}
          onExit={setPasswordExit}
        />
      </OidcAuthShell>
    );
  }

  if (mfaId) {
    return (
      <OidcAuthShell {...shellProps} heading={t("INVITATION.VERIFY_HEADING")}>
        <form
          className="flex flex-col gap-3 py-4"
          onSubmit={(e) => {
            e.preventDefault();
            mfaMutation.mutate(
              { mfaId, mfaCode },
              {
                onSuccess: onRedeemResult,
                onError: () =>
                  showErrorToast({ errors: t("INVITATION.INVALID_CODE") }),
              },
            );
          }}
        >
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            {t("INVITATION.ENTER_MFA_CODE", {
              type: userMfa ?? "MFA",
              application: contextQuery.data.applicationName,
            })}
          </p>
          <input
            className="rounded border px-3 py-2 text-sm"
            value={mfaCode}
            onChange={(e) => setMfaCode(e.target.value)}
            autoComplete="one-time-code"
            aria-label={t("INVITATION.MFA_CODE_ARIA")}
          />
          <button
            type="submit"
            className="rounded px-3 py-2 text-sm font-medium"
            style={{ background: "var(--accent)", color: "var(--bg)" }}
            disabled={mfaMutation.isPending || !mfaCode}
          >
            {t("common:CONTINUE")}
          </button>
        </form>
      </OidcAuthShell>
    );
  }

  if (activationKey) {
    // The password is set here, on the same page: the key stays in memory rather than in a URL,
    // and no email is ever sent for it.
    return (
      <Activation
        code={activationKey}
        tenantId={tenantId}
        collectPassword
        onActivated={followAuthorizeUrl}
      />
    );
  }

  if (contextQuery.data.credentialMode === "PasswordRequired") {
    return (
      <OidcAuthShell {...shellProps} heading={t("INVITATION.WELCOME", { name: contextQuery.data.firstName })}>
        <div className="flex flex-col gap-3 py-4">
          <p className="text-sm" style={{ color: "var(--fg)" }}>
            {t("INVITATION.CONTINUE_INTO", {
              application: contextQuery.data.applicationName,
              email: contextQuery.data.maskedEmail,
            })}
          </p>
          <div className="flex flex-col items-center gap-3 py-4">
            <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
            <p className="text-sm" style={{ color: "var(--muted)" }}>
              {t("INVITATION.PREPARING")}
            </p>
          </div>
        </div>
      </OidcAuthShell>
    );
  }

  return (
    <OidcAuthShell {...shellProps} heading={t("INVITATION.WELCOME", { name: contextQuery.data.firstName })}>
      <div className="flex flex-col items-center gap-3 py-8">
        <Loader className="h-8 w-8 animate-spin" style={{ color: "var(--accent)" }} />
        <p className="text-sm" style={{ color: "var(--muted)" }}>
          {t("INVITATION.SIGNING_IN", { application: contextQuery.data.applicationName })}
        </p>
      </div>
    </OidcAuthShell>
  );
}

export default InvitationPage;
