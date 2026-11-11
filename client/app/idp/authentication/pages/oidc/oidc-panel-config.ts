import { tStatic } from "@seliseblocks/genesis-os/i18n";
import type { OidcPanelConfig, OidcServiceNode } from "./nodes-panel-oidc";

const OIDC_LOGIN_PANEL_IDLE_BADGE = "OIDC_LOGIN_PANEL.IDLE_BADGE";

/**
 * A decorative terminal line whose prose is translated: `prefix` is the pseudo-protocol part
 * ("200 OK — ", "  > "), kept verbatim like the other command lines. The getter reads the key
 * at render time, so a language change is picked up without rebuilding the panel.
 */
function terminalProse(prefix: string, key: string, color: string): { text: string; color: string } {
  return {
    get text() { return prefix + tStatic("iam.oidc", key); },
    color,
  };
}

/* ──────────────────────────────────────────────────────────────
   OIDC LOGIN — real service flow:
     IAM Service validates credentials against POST /api/oidc/login →
     IAM issues authorization code (PKCE) →
     OIDC Service builds the redirect URI →
     Browser is redirected back to the client application
   ────────────────────────────────────────────────────────────── */
export const OIDC_LOGIN_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", OIDC_LOGIN_PANEL_IDLE_BADGE); },
  get submittingBadge() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.FAILED_BADGE"); },

  idleNode: {
    icon:        "cursor",
    get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.IDLE_TITLE"); },
    get description() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon:         "key",
    service:      "IAM Service",
    get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.VALIDATING_TITLE"); },
    activeLabel:  "POST /api/oidc/login …",
    get successLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [
    {
      icon:         "ticket",
      service:      "IAM Service",
      get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_1_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_1_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_1_SUCCESS"); },
    },
    {
      icon:         "shield-check",
      service:      "OIDC Service",
      get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_2_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_2_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_2_SUCCESS"); },
    },
    {
      icon:         "external",
      service:      "Browser",
      get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_3_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_3_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.NODE_3_SUCCESS"); },
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/oidc/login",               color: "var(--accent2)" },
    { text: "  > grant_type=authorization_code",    color: "var(--muted)"   },
    { text: "  > code_challenge_method=S256",        color: "var(--muted)"   },
    terminalProse("200 OK — ", "PANEL_TERMINAL.CREDENTIALS_VERIFIED", "var(--success)"),
    { text: "$ iam.issueAuthCode(client_id, scope)", color: "var(--fg)"      },
    { text: "  > code_challenge verified",           color: "var(--muted)"   },
    { text: "  > code=xxxxxxxx issued",              color: "var(--success)" },
    { text: "$ buildRedirectUri(redirect_uri, code)", color: "var(--fg)"    },
    terminalProse("  > ", "PANEL_TERMINAL.STATE_PRESERVED", "var(--muted)"),
    { text: "window.location → client app",          color: "var(--success)"},
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/oidc/login",            color: "var(--accent2)" },
    { text: "  > code_challenge_method=S256",     color: "var(--muted)"  },
  ],
};

/* ──────────────────────────────────────────────────────────────
   SIGNUP — real service flow:
     IAM Service validates email uniqueness (POST /api/account/signup) →
     IAM provisions a new user record →
     Mail Service dispatches the activation email to the inbox
   ────────────────────────────────────────────────────────────── */
export const SIGNUP_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "SIGNUP_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "SIGNUP_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", "SIGNUP_PANEL.IDLE_BADGE"); },
  get submittingBadge() { return tStatic("iam.oidc", "SIGNUP_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "SIGNUP_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "SIGNUP_PANEL.FAILED_BADGE"); },

  idleNode: {
    icon:        "cursor",
    get title() { return tStatic("iam.oidc", OIDC_LOGIN_PANEL_IDLE_BADGE); },
    get description() { return tStatic("iam.oidc", "SIGNUP_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon:         "shield-check",
    service:      "IAM Service",
    get title() { return tStatic("iam.oidc", "SIGNUP_PANEL.VALIDATING_TITLE"); },
    activeLabel:  "POST /api/account/signup …",
    get successLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [
    {
      icon:         "key",
      service:      "IAM Service",
      get title() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_1_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_1_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_1_SUCCESS"); },
    },
    {
      icon:         "external",
      service:      "Mail Service",
      get title() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_2_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_2_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "SIGNUP_PANEL.NODE_2_SUCCESS"); },
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/account/signup",           color: "var(--accent2)" },
    { text: "  > content-type: application/json",   color: "var(--muted)"   },
    terminalProse("200 OK — ", "PANEL_TERMINAL.EMAIL_ACCEPTED", "var(--success)"),
    { text: "$ iam.createAccount(email)",            color: "var(--fg)"      },
    { text: "  > user_id provisioned",               color: "var(--muted)"   },
    { text: "$ mail.sendActivation(email)",          color: "var(--fg)"      },
    { text: "  > template: account-activation",      color: "var(--muted)"   },
    terminalProse("", "PANEL_TERMINAL.ACTIVATION_EMAIL_DISPATCHED", "var(--success)"),
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/account/signup",          color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"  },
  ],
};

/* ──────────────────────────────────────────────────────────────
   RESET PASSWORD — real service flow:
     IAM Service validates the reset code (POST /api/account/reset-password) →
     IAM updates the password hash →
     IAM revokes active session tokens (when logoutFromAllDevices is on)
   ────────────────────────────────────────────────────────────── */
export const RESET_PASSWORD_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", OIDC_LOGIN_PANEL_IDLE_BADGE); },
  get submittingBadge() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.FAILED_BADGE"); },

  idleNode: {
    icon:        "cursor",
    get title() { return tStatic("iam.oidc", OIDC_LOGIN_PANEL_IDLE_BADGE); },
    get description() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon:         "key",
    service:      "IAM Service",
    get title() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.VALIDATING_TITLE"); },
    activeLabel:  "POST /api/account/reset-password …",
    get successLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [
    {
      icon:         "shield-check",
      service:      "IAM Service",
      get title() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_1_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_1_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_1_SUCCESS"); },
    },
    {
      icon:         "cookie",
      service:      "IAM Service",
      get title() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_2_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_2_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "RESET_PASSWORD_PANEL.NODE_2_SUCCESS"); },
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/account/reset-password",  color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"   },
    terminalProse("200 OK — ", "PANEL_TERMINAL.RESET_CODE_VERIFIED", "var(--success)"),
    { text: "$ iam.updatePassword(user_id)",        color: "var(--fg)"      },
    { text: "  > password_hash updated",            color: "var(--muted)"   },
    { text: "$ iam.revokeSessions(user_id)",        color: "var(--fg)"      },
    { text: "  > active_tokens revoked",            color: "var(--muted)"   },
    terminalProse("", "PANEL_TERMINAL.PASSWORD_RESET_COMPLETE", "var(--success)"),
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/account/reset-password", color: "var(--accent2)" },
    { text: "  > content-type: application/json", color: "var(--muted)"  },
  ],
};

const SETTING_PASSWORD_NODE: OidcServiceNode = {
  icon:         "shield-check",
  service:      "IAM Service",
  get title() { return tStatic("iam.oidc", "SETTING_PASSWORD_NODE.TITLE"); },
  get activeLabel() { return tStatic("iam.oidc", "SETTING_PASSWORD_NODE.ACTIVE"); },
  get successLabel() { return tStatic("iam.oidc", "SETTING_PASSWORD_NODE.SUCCESS"); },
};

const ACTIVATING_ACCOUNT_NODE: OidcServiceNode = {
  icon:         "user-check",
  service:      "IAM Service",
  get title() { return tStatic("iam.oidc", "ACTIVATING_ACCOUNT_NODE.TITLE"); },
  get activeLabel() { return tStatic("iam.oidc", "ACTIVATING_ACCOUNT_NODE.ACTIVE"); },
  get successLabel() { return tStatic("iam.oidc", "ACTIVATING_ACCOUNT_NODE.SUCCESS"); },
};

export const ACTIVATE_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "ACTIVATE_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "ACTIVATE_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", OIDC_LOGIN_PANEL_IDLE_BADGE); },
  get submittingBadge() { return tStatic("iam.oidc", "ACTIVATE_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "ACTIVATE_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "ACTIVATE_PANEL.FAILED_BADGE"); },

  idleNode: {
    icon:        "cursor",
    get title() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.IDLE_TITLE"); },
    get description() { return tStatic("iam.oidc", "ACTIVATE_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon:         "key",
    service:      "IAM Service",
    get title() { return tStatic("iam.oidc", "ACTIVATE_PANEL.VALIDATING_TITLE"); },
    activeLabel:  "POST /api/account/activate …",
    get successLabel() { return tStatic("iam.oidc", "ACTIVATE_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "ACTIVATE_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [SETTING_PASSWORD_NODE, ACTIVATING_ACCOUNT_NODE],

  terminalMessages: [
    { text: "$ POST /api/account/activate",        color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"   },
    terminalProse("200 OK — ", "PANEL_TERMINAL.ACTIVATION_CODE_VERIFIED", "var(--success)"),
    { text: "$ iam.updatePassword(user_id)",       color: "var(--fg)"      },
    { text: "  > password_hash updated",           color: "var(--muted)"   },
    { text: "$ iam.activateAccount(user_id)",      color: "var(--fg)"      },
    { text: "  > is_active = true",                color: "var(--muted)"   },
    terminalProse("", "PANEL_TERMINAL.ACCOUNT_ACTIVATION_COMPLETE", "var(--success)"),
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/account/activate",        color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"   },
  ],
};

/**
 * Same pipeline with the password step removed, for tenants whose IAM configuration turns
 * off `collectPasswordOnActivation`: confirming the emailed link is the whole flow.
 */
export const ACTIVATE_PANEL_WITHOUT_PASSWORD: OidcPanelConfig = {
  // Read through, never spread: a spread would freeze today's language into the copy.
  get heading() { return ACTIVATE_PANEL.heading; },
  get subtext() { return tStatic("iam.oidc", "ACTIVATE_PANEL_WITHOUT_PASSWORD.SUBTEXT"); },

  get idleBadge() { return ACTIVATE_PANEL.idleBadge; },
  get submittingBadge() { return ACTIVATE_PANEL.submittingBadge; },
  get successBadge() { return ACTIVATE_PANEL.successBadge; },
  get failedBadge() { return ACTIVATE_PANEL.failedBadge; },

  idleNode: {
    get icon() { return ACTIVATE_PANEL.idleNode.icon; },
    get title() { return ACTIVATE_PANEL.idleNode.title; },
    get description() { return tStatic("iam.oidc", "ACTIVATE_PANEL_WITHOUT_PASSWORD.IDLE_DESCRIPTION"); },
  },

  validatingNode: ACTIVATE_PANEL.validatingNode,

  successNodes: [ACTIVATING_ACCOUNT_NODE],

  terminalMessages: [
    { text: "$ POST /api/account/activate",        color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"   },
    terminalProse("200 OK — ", "PANEL_TERMINAL.ACTIVATION_CODE_VERIFIED", "var(--success)"),
    { text: "$ iam.activateAccount(user_id)",      color: "var(--fg)"      },
    { text: "  > is_active = true",                color: "var(--muted)"   },
    terminalProse("", "PANEL_TERMINAL.ACCOUNT_ACTIVATION_COMPLETE", "var(--success)"),
  ],
};

/** Picks the activation panel copy that matches the tenant's password setting. */
export const getActivatePanel = (collectPassword: boolean): OidcPanelConfig =>
  collectPassword ? ACTIVATE_PANEL : ACTIVATE_PANEL_WITHOUT_PASSWORD;


/* ──────────────────────────────────────────────────────────────
   JOIN (signup link) — context → redeem → authorize redirect
   ────────────────────────────────────────────────────────────── */
export const JOIN_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "JOIN_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "JOIN_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", "JOIN_PANEL.IDLE_BADGE"); },
  get submittingBadge() { return tStatic("iam.oidc", "JOIN_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "JOIN_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "JOIN_PANEL.FAILED_BADGE"); },

  idleNode: {
    icon: "cursor",
    get title() { return tStatic("iam.oidc", "JOIN_PANEL.IDLE_TITLE"); },
    get description() { return tStatic("iam.oidc", "JOIN_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon: "key",
    service: "IAM Service",
    get title() { return tStatic("iam.oidc", "JOIN_PANEL.VALIDATING_TITLE"); },
    activeLabel: "GET /api/iam/signup-links/context …",
    get successLabel() { return tStatic("iam.oidc", "JOIN_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "JOIN_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [
    {
      icon: "ticket",
      service: "IAM Service",
      get title() { return tStatic("iam.oidc", "JOIN_PANEL.NODE_1_TITLE"); },
      activeLabel: "POST /api/iam/signup-links/redeem …",
      get successLabel() { return tStatic("iam.oidc", "JOIN_PANEL.NODE_1_SUCCESS"); },
    },
    {
      icon: "external",
      service: "Browser",
      get title() { return tStatic("iam.oidc", "JOIN_PANEL.NODE_2_TITLE"); },
      get activeLabel() { return tStatic("iam.oidc", "JOIN_PANEL.NODE_2_ACTIVE"); },
      get successLabel() { return tStatic("iam.oidc", "JOIN_PANEL.NODE_2_SUCCESS"); },
    },
  ],

  terminalMessages: [
    { text: "$ GET /api/iam/signup-links/context", color: "var(--accent2)" },
    { text: "  > X-Signup-Link-Code: (header)", color: "var(--muted)" },
    terminalProse("200 OK — ", "PANEL_TERMINAL.LINK_VALID", "var(--success)"),
    { text: "$ POST /api/iam/signup-links/redeem", color: "var(--fg)" },
    terminalProse("  > ", "PANEL_TERMINAL.RESTRICTED_SESSION_COOKIE_SET", "var(--muted)"),
    { text: "window.location → authorizeUrl", color: "var(--success)" },
  ],

  errorTerminalPrefix: [
    { text: "$ GET /api/iam/signup-links/context", color: "var(--accent2)" },
  ],
};

/* ──────────────────────────────────────────────────────────────
   SIGN-IN ERROR — the provider sent the browser back, but IAM could
   not finish the sign-in (expired, already used, or refused).
   The page opens with this panel already in its failed state.
   ────────────────────────────────────────────────────────────── */
export const OIDC_ERROR_PANEL: OidcPanelConfig = {
  get heading() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.HEADING"); },
  get subtext() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.SUBTEXT"); },

  get idleBadge() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.IDLE_BADGE"); },
  get submittingBadge() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.SUBMITTING_BADGE"); },
  get successBadge() { return tStatic("iam.oidc", "OIDC_LOGIN_PANEL.SUCCESS_BADGE"); },
  get failedBadge() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.IDLE_BADGE"); },

  idleNode: {
    icon:        "shield-check",
    get title() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.IDLE_TITLE"); },
    get description() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.IDLE_DESCRIPTION"); },
  },

  validatingNode: {
    icon:         "key",
    service:      "IAM Service",
    get title() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.VALIDATING_TITLE"); },
    activeLabel:  "GET /api/oidc/callback …",
    get successLabel() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.VALIDATING_SUCCESS"); },
    get failLabel() { return tStatic("iam.oidc", "OIDC_ERROR_PANEL.VALIDATING_FAIL"); },
  },

  successNodes: [],

  terminalMessages: [],

  errorTerminalPrefix: [
    { text: "$ GET /api/oidc/callback",  color: "var(--accent2)" },
    terminalProse("  > ", "PANEL_TERMINAL.STATE_CHECKED", "var(--muted)"),
  ],
};
