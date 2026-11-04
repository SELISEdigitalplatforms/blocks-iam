import type { TFunction } from "@seliseblocks/genesis-os/i18n";
import type { OidcPanelConfig } from "@blocks-idp/authentication/pages/oidc/nodes-panel-oidc";

export const buildDeviceEntryPanel = (t: TFunction): OidcPanelConfig => ({
  heading: t("PANEL_CONFIG.ENTRY_HEADING"),
  subtext:
    t("PANEL_CONFIG.ENTRY_SUBTEXT"),

  idleBadge:       t("PANEL_CONFIG.ENTRY_IDLE_BADGE"),
  submittingBadge: t("PANEL_CONFIG.ENTRY_SUBMITTING_BADGE"),
  successBadge:    t("PANEL_CONFIG.ENTRY_SUCCESS_BADGE"),
  failedBadge:     t("PANEL_CONFIG.ENTRY_FAILED_BADGE"),

  idleNode: {
    icon:        "cursor",
    title:       t("PANEL_CONFIG.ENTRY_IDLE_TITLE"),
    description: t("PANEL_CONFIG.ENTRY_IDLE_DESCRIPTION"),
  },

  validatingNode: {
    icon:         "key",
    service:      "Device Service",
    title:        t("PANEL_CONFIG.ENTRY_VALIDATING_TITLE"),
    activeLabel:  "POST /api/device …",
    successLabel: t("PANEL_CONFIG.ENTRY_VALIDATING_SUCCESS"),
    failLabel:    t("PANEL_CONFIG.ENTRY_VALIDATING_FAIL"),
  },

  successNodes: [
    {
      icon:         "shield-check",
      service:      "Device Service",
      title:        t("PANEL_CONFIG.ENTRY_PREPARING_TITLE"),
      activeLabel:  t("PANEL_CONFIG.ENTRY_PREPARING_ACTIVE"),
      successLabel: t("PANEL_CONFIG.ENTRY_PREPARING_SUCCESS"),
    },
    {
      icon:         "external",
      service:      "Browser",
      title:        t("PANEL_CONFIG.ENTRY_CONTINUING_TITLE"),
      activeLabel:  t("PANEL_CONFIG.ENTRY_CONTINUING_ACTIVE"),
      successLabel: t("PANEL_CONFIG.ENTRY_CONTINUING_SUCCESS"),
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
});

export const buildDeviceConsentPanel = (t: TFunction): OidcPanelConfig => ({
  heading: t("PANEL_CONFIG.CONSENT_HEADING"),
  subtext:
    t("PANEL_CONFIG.CONSENT_SUBTEXT"),

  idleBadge:       t("PANEL_CONFIG.CONSENT_IDLE_BADGE"),
  submittingBadge: t("PANEL_CONFIG.CONSENT_SUBMITTING_BADGE"),
  successBadge:    t("PANEL_CONFIG.CONSENT_SUCCESS_BADGE"),
  failedBadge:     t("PANEL_CONFIG.CONSENT_FAILED_BADGE"),

  idleNode: {
    icon:        "cursor",
    title:       t("PANEL_CONFIG.CONSENT_IDLE_TITLE"),
    description: t("PANEL_CONFIG.CONSENT_IDLE_DESCRIPTION"),
  },

  validatingNode: {
    icon:         "key",
    service:      "Device Service",
    title:        t("PANEL_CONFIG.CONSENT_VALIDATING_TITLE"),
    activeLabel:  "GET /api/device/continue/{id} …",
    successLabel: t("PANEL_CONFIG.CONSENT_VALIDATING_SUCCESS"),
    failLabel:    t("PANEL_CONFIG.CONSENT_VALIDATING_FAIL"),
  },

  successNodes: [
    {
      icon:         "shield-check",
      service:      "Device Service",
      title:        t("PANEL_CONFIG.CONSENT_RECORDING_TITLE"),
      activeLabel:  t("PANEL_CONFIG.CONSENT_RECORDING_ACTIVE"),
      successLabel: t("PANEL_CONFIG.CONSENT_RECORDING_SUCCESS"),
    },
    {
      icon:         "external",
      service:      "Browser",
      title:        t("PANEL_CONFIG.CONSENT_MARKING_TITLE"),
      activeLabel:  t("PANEL_CONFIG.CONSENT_MARKING_ACTIVE"),
      successLabel: t("PANEL_CONFIG.CONSENT_MARKING_SUCCESS"),
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
});
