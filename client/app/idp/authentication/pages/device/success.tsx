import { useMemo } from "react";
import { Link, useParams, useSearchParams } from "react-router-dom";

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
  const params = useParams<{ tenantId: string }>();
  const [searchParams] = useSearchParams();
  const tenantId = (params.tenantId ?? "").trim();
  const outcome = useMemo<Outcome>(
    () => resolveOutcome(searchParams.get("outcome")),
    [searchParams],
  );

  const copy = useMemo(() => {
    switch (outcome) {
      case "approved":
        return {
          successTitle: "Device Authorized",
          successSubtitle: "You can return to your device — it has been authorized.",
        };
      case "denied":
        return {
          successTitle: "Authorization Declined",
          successSubtitle: "The device was not authorized. You can close this tab.",
        };
      case "expired":
        return {
          successTitle: "Session Expired",
          successSubtitle: "The device code expired before approval. Restart the flow on your device.",
        };
      default:
        return {
          successTitle: "You may close this tab",
          successSubtitle: "Device flow finished. You can return to your device.",
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
      footerNote={
        <p className="text-xs oidc-font-rajdhani" style={{ color: "var(--muted)" }}>
          {tenantId ? (
            <>
              Tenant:&nbsp;
              <span className="oidc-sci-fi-badge" style={{ marginLeft: 4 }}>
                {tenantId}
              </span>
            </>
          ) : null}
        </p>
      }
    >
      <div className="flex flex-col gap-4">
        <Link to={`/device/${tenantId}`} className="oidc-sci-fi-btn w-full text-center">
          Use another code
        </Link>
        <p
          className="text-xs oidc-font-rajdhani text-center"
          style={{ color: "var(--muted)" }}
        >
          You can safely close this window.
        </p>
      </div>
    </OidcAuthShell>
  );
}
