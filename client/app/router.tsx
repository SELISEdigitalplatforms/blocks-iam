import { createBrowserRouter, Navigate } from "react-router-dom";

import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";
import LoginPage from "./routes/login";

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

// IDP Admin routes (protected)
import { UsersListPage } from "./idp/admin/users/pages/users-list";
import { CreateUserPage } from "./idp/admin/users/pages/create-user";
import { UserDetailPage } from "./idp/admin/users/pages/user-detail";
import { OrganizationsListPage } from "./idp/admin/organizations/pages/organizations-list";
import { CreateOrganizationPage } from "./idp/admin/organizations/pages/create-organization";
import { OrganizationDetailPage } from "./idp/admin/organizations/pages/organization-detail";
import { OidcClientsListPage } from "./idp/admin/clients/pages/oidc-clients-list";
import { CreateOidcClientPage } from "./idp/admin/clients/pages/create-oidc-client";
import { OidcClientDetailPage } from "./idp/admin/clients/pages/oidc-client-detail";
import { SessionsListPage } from "./idp/admin/sessions/pages/sessions-list";
import { ActivitiesPage } from "./idp/admin/activities/pages/activities";

export const router = createBrowserRouter([
  // ── Public login page (legacy entrypoint) ──
  {
    path: "/login",
    element: <OidcLayout />,
    children: [{ index: true, element: <LoginPage /> }],
  },




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

  // ── IDP Admin (protected) ──
  {
    element: <DashboardLayout />,
    children: [
      // Users
      { path: "/idp/admin/users", element: <UsersListPage /> },
      { path: "/idp/admin/users/create", element: <CreateUserPage /> },
      { path: "/idp/admin/users/:userId", element: <UserDetailPage /> },

      // Organizations
      { path: "/idp/admin/organizations", element: <OrganizationsListPage /> },
      { path: "/idp/admin/organizations/create", element: <CreateOrganizationPage /> },
      { path: "/idp/admin/organizations/:organizationId", element: <OrganizationDetailPage /> },

      // OIDC Clients
      { path: "/idp/admin/clients", element: <OidcClientsListPage /> },
      { path: "/idp/admin/clients/create", element: <CreateOidcClientPage /> },
      { path: "/idp/admin/clients/:clientId", element: <OidcClientDetailPage /> },

      // Sessions & Activities
      { path: "/idp/admin/sessions", element: <SessionsListPage /> },
      { path: "/idp/admin/activities", element: <ActivitiesPage /> },

      // Legacy redirects
      { path: "/identity", element: <Navigate to="/idp/admin/users" replace /> },
      { path: "/identity/users", element: <Navigate to="/idp/admin/users" replace /> },
      { path: "/identity/organizations", element: <Navigate to="/idp/admin/organizations" replace /> },
      { path: "/identity/clients", element: <Navigate to="/idp/admin/clients" replace /> },
      { path: "/services/authentication", element: <Navigate to="/idp/admin/users" replace /> },
      { path: "/services/authentication/users", element: <Navigate to="/idp/admin/users" replace /> },
      { path: "/services/authentication/organizations", element: <Navigate to="/idp/admin/organizations" replace /> },
      { path: "/services/authentication/client-credential", element: <Navigate to="/idp/admin/clients" replace /> },
    ],
  },

  // ── Root redirect to public page ──
  { path: "/", element: <Navigate to="/login" replace /> },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
], {});
