import type { OidcPanelConfig } from "./nodes-panel-oidc";

/* ──────────────────────────────────────────────────────────────
   OIDC LOGIN — real service flow:
     IAM Service validates credentials against POST /api/oidc/login →
     IAM issues authorization code (PKCE) →
     OIDC Service builds the redirect URI →
     Browser is redirected back to the client application
   ────────────────────────────────────────────────────────────── */
export const OIDC_LOGIN_PANEL: OidcPanelConfig = {
  heading: "OIDC Authentication Pipeline",
  subtext:
    "Your credentials are verified against the Blocks IAM service. An authorization code is issued and your browser is redirected to the client application.",

  idleBadge:       "Awaiting Input",
  submittingBadge: "Verifying",
  successBadge:    "Authenticated",
  failedBadge:     "Rejected",

  idleNode: {
    icon:        "cursor",
    title:       "Awaiting User Input",
    description: "Enter your email and password to begin OIDC authentication.",
  },

  validatingNode: {
    icon:         "key",
    service:      "IAM Service",
    title:        "Validating Credentials",
    activeLabel:  "POST /api/oidc/login …",
    successLabel: "Credentials verified",
    failLabel:    "Credentials rejected by IAM",
  },

  successNodes: [
    {
      icon:         "ticket",
      service:      "IAM Service",
      title:        "Issuing Auth Code",
      activeLabel:  "Generating authorization code (PKCE, S256)…",
      successLabel: "Authorization code issued",
    },
    {
      icon:         "shield-check",
      service:      "OIDC Service",
      title:        "Building Redirect URI",
      activeLabel:  "Attaching code + state to redirect_uri…",
      successLabel: "Redirect URI prepared",
    },
    {
      icon:         "external",
      service:      "Browser",
      title:        "Redirecting to Client",
      activeLabel:  "Sending browser to client application…",
      successLabel: "Authorization code delivered",
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/oidc/login",               color: "var(--accent2)" },
    { text: "  > grant_type=authorization_code",    color: "var(--muted)"   },
    { text: "  > code_challenge_method=S256",        color: "var(--muted)"   },
    { text: "200 OK — credentials verified",         color: "var(--success)" },
    { text: "$ iam.issueAuthCode(client_id, scope)", color: "var(--fg)"      },
    { text: "  > code_challenge verified",           color: "var(--muted)"   },
    { text: "  > code=xxxxxxxx issued",              color: "var(--success)" },
    { text: "$ buildRedirectUri(redirect_uri, code)", color: "var(--fg)"    },
    { text: "  > state preserved",                   color: "var(--muted)"  },
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
  heading: "Account Registration Pipeline",
  subtext:
    "Your email is validated against the Blocks IAM service. A new user record is provisioned and an activation link is dispatched to your inbox.",

  idleBadge:       "Ready",
  submittingBadge: "Registering",
  successBadge:    "Account Created",
  failedBadge:     "Registration Failed",

  idleNode: {
    icon:        "cursor",
    title:       "Awaiting Input",
    description: "Enter your email address to create a Blocks account.",
  },

  validatingNode: {
    icon:         "shield-check",
    service:      "IAM Service",
    title:        "Validating Email",
    activeLabel:  "POST /api/account/signup …",
    successLabel: "Email accepted",
    failLabel:    "Registration rejected by IAM",
  },

  successNodes: [
    {
      icon:         "key",
      service:      "IAM Service",
      title:        "Creating Account",
      activeLabel:  "Provisioning user record…",
      successLabel: "Account created successfully",
    },
    {
      icon:         "external",
      service:      "Mail Service",
      title:        "Sending Activation Email",
      activeLabel:  "Dispatching verification link…",
      successLabel: "Activation email sent",
    },
  ],

  terminalMessages: [
    { text: "$ POST /api/account/signup",           color: "var(--accent2)" },
    { text: "  > content-type: application/json",   color: "var(--muted)"   },
    { text: "200 OK — email accepted",               color: "var(--success)" },
    { text: "$ iam.createAccount(email)",            color: "var(--fg)"      },
    { text: "  > user_id provisioned",               color: "var(--muted)"   },
    { text: "$ mail.sendActivation(email)",          color: "var(--fg)"      },
    { text: "  > template: account-activation",      color: "var(--muted)"   },
    { text: "activation email dispatched",           color: "var(--success)" },
  ],

  errorTerminalPrefix: [
    { text: "$ POST /api/account/signup",          color: "var(--accent2)" },
    { text: "  > content-type: application/json",  color: "var(--muted)"  },
  ],
};
