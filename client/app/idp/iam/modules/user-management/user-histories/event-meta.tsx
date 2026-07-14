import {
  KeyRound,
  LogIn,
  LogOut,
  RefreshCw,
  ShieldCheck,
  XCircle,
  type LucideIcon,
} from "lucide-react";

export type EventTone = "success" | "info" | "error" | "warning";

export type EventMeta = {
  label: string;
  description: string;
  icon: LucideIcon;
  tone: EventTone;
};

export const EVENT_META: Record<string, EventMeta> = {
  login_via_password: {
    label: "Sign in",
    description: "Interactive sign-in",
    icon: LogIn,
    tone: "success",
  },
  login_via_social: {
    label: "Sign in",
    description: "Signed in via social login",
    icon: LogIn,
    tone: "success",
  },
  login_via_sso_consent: {
    label: "Sign in",
    description: "Signed in via SSO",
    icon: LogIn,
    tone: "success",
  },
  login_via_authorization_code: {
    label: "Sign in",
    description: "Signed in via authorization code",
    icon: LogIn,
    tone: "success",
  },
  login_via_mfa_code: {
    label: "MFA challenge",
    description: "MFA verified",
    icon: ShieldCheck,
    tone: "success",
  },
  token_renewed: {
    label: "Token refreshed",
    description: "Session token refreshed",
    icon: RefreshCw,
    tone: "info",
  },
  renew_refresh_token: {
    label: "Token refreshed",
    description: "Refresh token renewed",
    icon: RefreshCw,
    tone: "info",
  },
  issued_refresh_token: {
    label: "Token issued",
    description: "Refresh token issued",
    icon: KeyRound,
    tone: "info",
  },
  session_revoked: {
    label: "Sign out",
    description: "Signed out from session",
    icon: LogOut,
    tone: "error",
  },
  revoke_access_by_logout: {
    label: "Sign out",
    description: "Signed out",
    icon: LogOut,
    tone: "error",
  },
  revoke_access_by_logout_all: {
    label: "Sign out",
    description: "Signed out of all devices",
    icon: LogOut,
    tone: "error",
  },
  revoke_refresh_token: {
    label: "Token revoked",
    description: "Refresh token revoked",
    icon: XCircle,
    tone: "error",
  },
  LOGIN_SUCCESS: {
    label: "Sign in",
    description: "Interactive sign-in",
    icon: LogIn,
    tone: "success",
  },
  LOGIN_FAILURE: {
    label: "Sign in failed",
    description: "Sign-in attempt failed",
    icon: XCircle,
    tone: "error",
  },
  TOKEN_REFRESHED: {
    label: "Token refreshed",
    description: "Session token refreshed",
    icon: RefreshCw,
    tone: "info",
  },
  SESSION_REVOKED: {
    label: "Sign out",
    description: "Signed out from session",
    icon: LogOut,
    tone: "error",
  },
  MFA_CHALLENGE_SUCCESS: {
    label: "MFA challenge",
    description: "MFA verified",
    icon: ShieldCheck,
    tone: "success",
  },
  PASSWORD_CHANGED: {
    label: "Password changed",
    description: "Account password updated",
    icon: KeyRound,
    tone: "info",
  },
};

export const getEventMeta = (event: string): EventMeta =>
  EVENT_META[event] ?? {
    label: event
      .split("_")
      .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
      .join(" "),
    description: event,
    icon: RefreshCw,
    tone: "info",
  };

export const EVENT_TONE_CLASS: Record<EventTone, string> = {
  success: "text-emerald-600 dark:text-emerald-400",
  info: "text-blue-600 dark:text-blue-400",
  error: "text-red-600 dark:text-red-400",
  warning: "text-amber-600 dark:text-amber-400",
};
