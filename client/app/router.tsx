import { createBrowserRouter, Navigate } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";
import { ConsoleLayout } from "./layouts/console-layout";
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";

// Auth routes (public, with auth layout)
import LoginPage from "./routes/auth/login";
import SignupPage from "./routes/auth/signup";
import SsoActivatePage from "./routes/auth/sso-activate";

// Public routes (with public guard only)
import ActivatePage from "./routes/auth/activate";
import ForgotPasswordPage from "./routes/auth/forgot-password";
import ResetPasswordPage from "./routes/auth/resetpassword";
import ActivateSuccessPage from "./routes/auth/activate-success";
import ForgotEmailSentPage from "./routes/auth/forgot-email-sent";
import SignupEmailSentPage from "./routes/auth/signup-email-sent";
import MfaCheckPage from "./routes/auth/mfa-check";
import ResetPasswordSuccessPage from "./routes/auth/reset-password-success";

// OIDC routes (un-guarded)
import OidcIndexPage from "./routes/oidc/index";
import OidcLoginPage from "./routes/oidc/login";
import OidcPermissionPage from "./routes/oidc/permission";
import OidcErrorPage from "./routes/oidc/error";
// import OidcForgotPasswordPage from "./routes/oidc/forgot-password";
import OidcEmailSentConfirmationPage from "./routes/oidc/email-sent-confirmation";

// Dashboard routes (protected)
import IamPage from "./routes/dashboard/iam";
import IamUserDetailPage from "./routes/dashboard/iam-user-detail";
import IamRoleDetailPage from "./routes/dashboard/iam-role-detail";
import IamPermissionDetailPage from "./routes/dashboard/iam-permission-detail";
import IamAddPermissionPage from "./routes/dashboard/iam-add-permission";
import IamOrgDetailPage from "./routes/dashboard/iam-org-detail";
import IamLogsPage from "./routes/dashboard/iam-logs";
import IamConfigurePage from "./routes/dashboard/iam-configure";
import AuthenticationConfigPage from "./routes/dashboard/authentication-config";
import SsoConfigurationPage from "./routes/dashboard/sso-configuration";
import AuthLogsPage from "./routes/dashboard/auth-logs";
import MfaLogsPage from "./routes/dashboard/mfa-logs";
import CaptchaLogsPage from "./routes/dashboard/captcha-logs";
import RateLimiterPage from "./routes/dashboard/rate-limiter";
import ManagedServicesPage from "./routes/dashboard/managed-services";
import ProfilePage from "./routes/dashboard/profile";

// Console pages
import { DashboardOverview } from "./pages/dashboard/dashboard-overview";
import { EnvironmentsPage } from "./pages/environments/environments";
import { PeopleManagement } from "./pages/people/people-management";
import { RepositoriesPage } from "./pages/repositories/repositories";
import { SettingsPage } from "./pages/settings/settings";
import { CreateProjectWrapper } from "./pages/create-project/create-project";
import CallbackPage from "./routes/callback/callback";
import { Console } from "./pages/console/console";
import OidcLogin from "./routes/auth/oidc-login";
import LoginSimplePage from "./routes/auth/login-simple";
import LoginCallbackPage from "./routes/auth/callback";
import SSOCallbackPage from "./routes/auth/sso-callback";

export const router = createBrowserRouter([
  // ── Auth layout (login, signup, sso-activate) ──
  {
    element: <AuthLayout />,
    children: [
      // { path: "/login", element: <LoginPage /> },
      { path: "/signup", element: <SignupPage /> },
      { path: "/sso-activate", element: <SsoActivatePage /> },
    ],
  },
  // ── Simple login (no guards, no API calls) ──
  // { path: "/login", element: <OidcLogin /> },

  {
    path: "/login",
    children: [
      { index: true, element: <LoginSimplePage /> },
      { path: "callback", element: <LoginCallbackPage /> },
    ],
  },

  {
    path: "/sso",
    children: [{ path: ":provider/callback", element: <SSOCallbackPage /> }],
  },

  // ── Public layout (other public pages with PublicGuard) ──
  {
    element: <PublicLayout />,
    children: [
      { path: "/activate", element: <ActivatePage /> },
      { path: "/forgot-password", element: <ForgotPasswordPage /> },
      { path: "/resetpassword", element: <ResetPasswordPage /> },
      { path: "/activate-success", element: <ActivateSuccessPage /> },
      { path: "/forgot-email-sent", element: <ForgotEmailSentPage /> },
      { path: "/signup-email-sent", element: <SignupEmailSentPage /> },
      { path: "/mfa-check", element: <MfaCheckPage /> },
      {
        path: "/reset-password-success",
        element: <ResetPasswordSuccessPage />,
      },
    ],
  },

  // ── OIDC layout (un-guarded, themed) ──
  {
    path: "/oidc",
    element: <OidcLayout />,
    children: [
      { index: true, element: <OidcIndexPage /> },
      { path: "login", element: <OidcLoginPage /> },
      { path: "permission", element: <OidcPermissionPage /> },
      { path: "error", element: <OidcErrorPage /> },
      // { path: "forgot-password", element: <OidcForgotPasswordPage /> },
      {
        path: "email-sent-confirmation",
        element: <OidcEmailSentConfirmationPage />,
      },
    ],
  },

  // ── Dashboard layout (protected routes) ──
  {
    element: <DashboardLayout />,
    children: [
      { path: "/services/iam", element: <IamPage /> },
      { path: "/services/iam/user-detail/:id", element: <IamUserDetailPage /> },
      { path: "/services/iam/role-detail/:id", element: <IamRoleDetailPage /> },
      {
        path: "/services/iam/permission-detail/new",
        element: <IamAddPermissionPage />,
      },
      {
        path: "/services/iam/permission-detail/:id",
        element: <IamPermissionDetailPage />,
      },
      {
        path: "/services/iam/organization-detail/:itemId",
        element: <IamOrgDetailPage />,
      },
      { path: "/services/iam/logs", element: <IamLogsPage /> },
      { path: "/services/iam/configure", element: <IamConfigurePage /> },
      {
        path: "/services/authentication/users",
        element: <AuthenticationConfigPage section="users" />,
      },
      {
        path: "/services/authentication/organizations",
        element: <AuthenticationConfigPage section="organizations" />,
      },
      {
        path: "/services/authentication/client-credential",
        element: <AuthenticationConfigPage section="client-credential" />,
      },
      {
        path: "/services/authentication/sso-configuration",
        element: <SsoConfigurationPage />,
      },
      { path: "/services/authentication/logs", element: <AuthLogsPage /> },
      { path: "/services/mfa/logs", element: <MfaLogsPage /> },
      { path: "/services/rate-limiter", element: <RateLimiterPage /> },
      { path: "/managed-services", element: <ManagedServicesPage /> },
      { path: "/services/captcha/logs", element: <CaptchaLogsPage /> },
      { path: "/dashboard", element: <DashboardOverview /> },
      {
        path: "/project-overview",
        element: <Navigate to="/project-overview/environments" replace />,
      },
      { path: "/project-overview/environments", element: <EnvironmentsPage /> },
      // { path: "/project-overview/people", element: <PeopleManagement /> },
      // { path: "/project-overview/repositories", element: <RepositoriesPage /> },
      // { path: "/project-overview/settings", element: <SettingsPage /> },
    ],
  },

  // ── Console layout (profile, create-project, callback without sidebar) ──
  {
    element: <ConsoleLayout />,
    children: [
      { path: "/profile", element: <ProfilePage /> },
      { path: "/console", element: <Console /> },
      { path: "/create-project", element: <CreateProjectWrapper /> },
      { path: "/callback", element: <CallbackPage /> },
    ],
  },

  // ── Root redirect: authenticated users go to authentication/users ──
  {
    path: "/",
    element: <Navigate to="/services/authentication/users" replace />,
  },

  // ── Catch-all: redirect to login ──
  { path: "*", element: <Navigate to="/login" replace /> },
]);
