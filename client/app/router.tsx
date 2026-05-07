import { createBrowserRouter, Navigate } from "react-router-dom";

import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";

// OIDC routes (un-guarded)
import OidcIndexPage from "./routes/oidc/index";
import OidcLoginPage from "./routes/oidc/login";
import OidcPermissionPage from "./routes/oidc/permission";
import OidcErrorPage from "./routes/oidc/error";
import OidcEmailSentConfirmationPage from "./routes/oidc/email-sent-confirmation";
import OidcSelectAccountPage from "./routes/oidc/select-account";
import OidcSignupPage from "./routes/oidc/signup";
import OidcSsoActivatePage from "./routes/oidc/sso-activate";
import { OidcMfaCheck } from "./idp/authentication/pages/oidc/oidc-mfa-check";
import { OidcForgotPassword } from "./idp/authentication/pages/oidc/oidc-forgot-password";
import { OidcActivation } from "./idp/authentication/pages/oidc/oidc-activation";

// Dashboard routes (protected)
import AuthenticationConfigPage from "./routes/dashboard/authentication-config";
import OidcLogin from "./routes/auth/oidc-login";

export const router = createBrowserRouter([
  // ── Simple login (no guards, no API calls) ──
  { path: "/login", element: <OidcLogin /> },




  // ── OIDC layout (un-guarded, themed) ──
  {
    path: "/oidc",
    element: <OidcLayout />,
    children: [
      { index: true, element: <OidcIndexPage /> },
      { path: "login", element: <OidcLoginPage /> },
      { path: "permission", element: <OidcPermissionPage /> },
      { path: "select-account", element: <OidcSelectAccountPage /> },
      { path: "error", element: <OidcErrorPage /> },
      { path: "email-sent-confirmation", element: <OidcEmailSentConfirmationPage /> },
      { path: "mfa-check", element: <OidcMfaCheck /> },
      { path: "forgot-password", element: <OidcForgotPassword /> },
      { path: "activation", element: <OidcActivation /> },
      { path: "signup", element: <OidcSignupPage /> },
      { path: "sso-activate", element: <OidcSsoActivatePage /> },
    ],
  },

  // ── Dashboard layout (protected routes) - DISABLED
  /*
  {
    element: <DashboardLayout />,
    children: [
      { path: "/services/iam", element: <IamPage /> },
      { path: "/services/iam/user-detail/:id", element: <IamUserDetailPage /> },
      { path: "/services/iam/role-detail/:id", element: <IamRoleDetailPage /> },
      { path: "/services/iam/permission-detail/new", element: <IamAddPermissionPage /> },
      { path: "/services/iam/permission-detail/:id", element: <IamPermissionDetailPage /> },
      { path: "/services/iam/organization-detail/:itemId", element: <IamOrgDetailPage /> },
      { path: "/services/iam/logs", element: <IamLogsPage /> },
      { path: "/services/iam/configure", element: <IamConfigurePage /> },
      { path: "/services/authentication", element: <Navigate to="/services/authentication/users" replace /> },
      { path: "/services/authentication/users", element: <AuthenticationConfigPage section="users" /> },
      { path: "/services/authentication/organizations", element: <AuthenticationConfigPage section="organizations" /> },
      { path: "/services/authentication/client-credential", element: <AuthenticationConfigPage section="client-credential" /> },
      { path: "/services/authentication/sso-configuration", element: <SsoConfigurationPage /> },
      { path: "/services/authentication/logs", element: <AuthLogsPage /> },
      { path: "/services/mfa", element: <Navigate to="/services/secret-management?tab=mfa" replace /> },
      { path: "/services/mfa/logs", element: <MfaLogsPage /> },
      // { path: "/services/api-settings", element: <ApiSettingsPage /> }, // DISABLED: Missing @blocks-idp/api-settings module
      { path: "/services/rate-limiter", element: <RateLimiterPage /> },
      { path: "/services/lmt", element: <LmtPage /> },
      { path: "/services/lmt/logs/:serviceName", element: <LmtServiceLogsPage /> },
      { path: "/services/secret-management", element: <SecretManagementPage /> },
      { path: "/services/secret-management/ai-models/:provider", element: <AiModelSelectedRoute /> },
      { path: "/managed-services", element: <ManagedServicesPage /> },
      { path: "/services/captcha", element: <Navigate to="/services/secret-management?tab=captcha" replace /> },
      { path: "/services/captcha/logs", element: <CaptchaLogsPage /> },
    ],
  },
  */

  // ── Identity Management (protected) ──
  {
    element: <DashboardLayout />,
    children: [
      { path: "/identity", element: <Navigate to="/identity/users" replace /> },
      { path: "/identity/users", element: <AuthenticationConfigPage section="users" /> },
      { path: "/identity/organizations", element: <AuthenticationConfigPage section="organizations" /> },
      { path: "/identity/clients", element: <AuthenticationConfigPage section="client-credential" /> },
      // Legacy route aliases for backward compatibility
      { path: "/services/authentication", element: <Navigate to="/identity/users" replace /> },
      { path: "/services/authentication/users", element: <Navigate to="/identity/users" replace /> },
      { path: "/services/authentication/organizations", element: <Navigate to="/identity/organizations" replace /> },
      { path: "/services/authentication/client-credential", element: <Navigate to="/identity/clients" replace /> },
    ],
  },

  // ── Root redirect: authenticated users go to identity management ──
  { path: "/", element: <Navigate to="/identity/users" replace /> },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
]);
