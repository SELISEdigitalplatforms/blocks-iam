import { useEffect, useMemo, useRef } from "react";
import { useSearchParams } from "react-router";
import { XCircle } from "lucide-react";

import { OidcAuthShell, useOidcAuthAnimation } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { DEVICE_CONSENT_PANEL } from "./panel-config";

type Outcome = "approved" | "denied" | "expired" | "neutral";

function resolveOutcome(raw: string | null): Outcome {
  if (raw === "approved") return "approved";
  if (raw === "denied") return "denied";
  if (raw === "expired") return "expired";
  return "neutral";
}

/* Rendered as the shell's children, so it sits below the anim Provider and can
   actually see the context — replays the pipeline animation to match the panel's
   terminal log, then settles into a state matching the real outcome. */
function DeviceSuccessBody({ outcome }: { outcome: Outcome }) {
  const animCtx = useOidcAuthAnimation();
  const startedRef = useRef(false);

  useEffect(() => {
    if (startedRef.current) return;
    startedRef.current = true;
    animCtx?.startAnimation();
    if (outcome === "denied") {
      void animCtx?.failAnimation("Authorization declined");
    } else if (outcome === "expired") {
      void animCtx?.failAnimation("Device code expired");
    } else {
      void animCtx?.succeedAnimation();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  if (outcome === "denied" || outcome === "expired") {
    return (
      <div className="flex flex-col items-center text-center gap-3 oidc-animate-fade-up">
        <div
          className="w-14 h-14 rounded-full flex items-center justify-center"
          style={{ background: "var(--danger-soft)", border: "1px solid var(--danger-border)" }}
        >
          <XCircle size={26} style={{ color: "var(--danger)" }} />
        </div>
        <p className="text-sm oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          You can safely close this window.
        </p>
      </div>
    );
  }

  return (
    <p className="text-sm oidc-font-rajdhani text-center" style={{ color: "var(--muted)" }}>
      You can safely close this window.
    </p>
  );
}

export function DeviceSuccessPage() {
  const [searchParams] = useSearchParams();
  const outcome = useMemo<Outcome>(
    () => resolveOutcome(searchParams.get("outcome")),
    [searchParams],
  );

  const copy = useMemo(() => {
    switch (outcome) {
      case "approved":
        return {
          successTitle: "Success",
          successSubtitle: "Your device has been authorized. You can close this window.",
        };
      case "denied":
        return {
          successTitle: "Authorization Declined",
          successSubtitle: "The device was not authorized. You can close this window.",
        };
      case "expired":
        return {
          successTitle: "Session Expired",
          successSubtitle: "The device code expired before approval. You can close this window.",
        };
      default:
        return {
          successTitle: "Success",
          successSubtitle: "Device flow finished. You can close this window.",
        };
    }
  }, [outcome]);

  return (
    <OidcAuthShell
      panelConfig={DEVICE_CONSENT_PANEL}
      heading={
        outcome === "denied"
          ? "Authorization Declined"
          : outcome === "expired"
          ? "Session Expired"
          : "Device Flow Complete"
      }
      headingDimFirst={3}
      showCorners={false}
      successTitle={copy.successTitle}
      successSubtitle={copy.successSubtitle}
    >
      <DeviceSuccessBody outcome={outcome} />
    </OidcAuthShell>
  );
}
