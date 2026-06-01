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
