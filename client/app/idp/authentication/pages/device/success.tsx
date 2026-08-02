import { useMemo } from "react";
import { useSearchParams } from "react-router";

import { OidcAuthShell } from "@blocks-idp/authentication/pages/oidc/oidc-auth-shell";
import { DEVICE_CONSENT_PANEL } from "./panel-config";

type Outcome = "approved" | "denied" | "expired" | "neutral";

function resolveOutcome(raw: string | null): Outcome {
  if (raw === "approved") return "approved";
  if (raw === "denied") return "denied";
  if (raw === "expired") return "expired";
  return "neutral";
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
      heading={outcome === "denied" ? "Authorization Declined" : "Device Flow Complete"}
      headingDimFirst={3}
      showCorners={false}
      successTitle={copy.successTitle}
      successSubtitle={copy.successSubtitle}
    >
      <p className="text-sm oidc-font-rajdhani text-center" style={{ color: "var(--muted)" }}>
        You can safely close this window.
      </p>
    </OidcAuthShell>
  );
}
