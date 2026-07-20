import type { OidcPanelConfig } from "@blocks-idp/authentication/pages/oidc/nodes-panel-oidc";

export const DEVICE_ENTRY_PANEL: OidcPanelConfig = {
  heading: "Device Sign-In Pipeline",
  subtext:
    "Your verification code is matched against an active device authorization request. Once resolved, the browser is redirected to consent or to the OIDC login flow.",

  idleBadge:       "Awaiting Code",
  submittingBadge: "Validating",
  successBadge:    "Resolved",
  failedBadge:     "Rejected",

  idleNode: {
    icon:        "cursor",
    title:       "Awaiting Verification Code",
    description: "Enter the code displayed on your device to continue.",
  },

  validatingNode: {
    icon:         "key",
    service:      "Device Service",
    title:        "Resolving User Code",
    activeLabel:  "POST /api/device …",
    successLabel: "User code resolved",
    failLabel:    "Code rejected by Device Service",
  },

  successNodes: [
    {
      icon:         "shield-check",
      service:      "Device Service",
      title:        "Preparing Interaction",
      activeLabel:  "Allocating interactionId…",
      successLabel: "Interaction prepared",
    },
    {
      icon:         "external",
      service:      "Browser",
      title:        "Continuing",
      activeLabel:  "Redirecting to consent or sign-in…",
      successLabel: "Redirect delivered",
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/device",                 color: "var(--accent2)" },
    { text: "  > content-type: application/json", color: "var(--muted)"   },
    { text: "  > X-Blocks-Key: <tenantId>",       color: "var(--muted)"   },
    { text: "200 OK — interaction prepared",      color: "var(--success)" },
    { text: "$ device.beginInteraction(code)",    color: "var(--fg)"      },
    { text: "  > interactionId issued",           color: "var(--muted)"   },
    { text: "window.location → /device/continue", color: "var(--success)" },
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/device",                color: "var(--accent2)" },
    { text: "  > X-Blocks-Key: <tenantId>",      color: "var(--muted)"  },
  ],
};

export const DEVICE_CONSENT_PANEL: OidcPanelConfig = {
  heading: "Device Consent Pipeline",
  subtext:
    "The browser fetches the consent payload for the device interaction. After approval the request is marked Approved and the device is authorized to exchange its code for tokens.",

  idleBadge:       "Awaiting Decision",
  submittingBadge: "Recording",
  successBadge:    "Authorized",
  failedBadge:     "Authorization Failed",

  idleNode: {
    icon:        "cursor",
    title:       "Awaiting User Decision",
    description: "Allow or deny the device's request to act on your behalf.",
  },

  validatingNode: {
    icon:         "key",
    service:      "Device Service",
    title:        "Loading Consent",
    activeLabel:  "GET /api/device/continue/{id} …",
    successLabel: "Consent payload loaded",
    failLabel:    "Consent payload rejected",
  },

  successNodes: [
    {
      icon:         "shield-check",
      service:      "Device Service",
      title:        "Recording Approval",
      activeLabel:  "Marking device authorization request…",
      successLabel: "Approval recorded",
    },
    {
      icon:         "external",
      service:      "Browser",
      title:        "Marking Device Authorized",
      activeLabel:  "Notifying browser of result…",
      successLabel: "Authorization delivered",
    },
  ],

  terminalMessages: [
    { text: "$ GET /api/device/continue/{id}",     color: "var(--accent2)" },
    { text: "  > X-Blocks-Key: <tenantId>",        color: "var(--muted)"   },
    { text: "200 OK — consent payload loaded",     color: "var(--success)" },
    { text: "$ POST /api/device/approve",          color: "var(--accent2)" },
    { text: "  > decision=allow",                  color: "var(--muted)"   },
    { text: "  > X-Blocks-Key: <tenantId>",        color: "var(--muted)"   },
    { text: "200 OK — request marked Approved",    color: "var(--success)" },
    { text: "window.location → /device/success",   color: "var(--success)" },
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/device/approve",         color: "var(--accent2)" },
    { text: "  > decision=allow",                 color: "var(--muted)"  },
  ],
};
