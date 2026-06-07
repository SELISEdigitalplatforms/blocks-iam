import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { PublicLayout } from "./layouts/public-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { DashboardLayout } from "./layouts/dashboard-layout";

// Auth routes (public, with auth layout)
import SignupPage from "./routes/auth/signup";
import SsoActivatePage from "./routes/auth/sso-activate";
import SSOCallbackPage from "./routes/auth/sso-callback";

// Public routes
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
import { CreateProjectWrapper } from "./pages/create-project/create-project";
import LoginSimplePage from "./routes/auth/login-simple";

import {
  AuthResolver,
  PublicGuard,
  ProtectedGuard,
  ConsoleLayout,
  ImpersonationChecker,
  ImpersonationTerminator,
  ImpersonationSynchronizer,
  ConsolePage,
  CallbackPage,
} from "@seliseblocks/blocks-kit";
import { ProjectOverviewLayout } from "./layouts/project-overview-layout";

export const router = createBrowserRouter([
  {
    element: <Outlet />,
    children: [
      // ── Callbacks outside AuthResolver ──
      {
        element: <Outlet />,
        children: [
          {
            path: "/login/callback",
            element: <CallbackPage redirectUrl="/console" />,
          },
          { path: "/sso/:provider/callback", element: <SSOCallbackPage /> },
        ],
      },

      // ── Everything inside AuthResolver (resolves auth state) ──
      {
        element: (
          <AuthResolver>
            <Outlet />
          </AuthResolver>
        ),
        children: [
          // ── OIDC layout (un-guarded, themed) ──

          // ── Public routes (unauthenticated only) ──
          {
            element: (
              <PublicGuard>
                <Outlet />
              </PublicGuard>
            ),
            children: [
              { path: "/login", element: <LoginSimplePage /> },
              {
                element: <AuthLayout />,
                children: [
                  { path: "/signup", element: <SignupPage /> },
                  { path: "/sso-activate", element: <SsoActivatePage /> },
                ],
              },
              {
                element: <PublicLayout />,
                children: [
                  { path: "/activate", element: <ActivatePage /> },
                  { path: "/forgot-password", element: <ForgotPasswordPage /> },
                  { path: "/resetpassword", element: <ResetPasswordPage /> },
                  {
                    path: "/activate-success",
                    element: <ActivateSuccessPage />,
                  },
                  {
                    path: "/forgot-email-sent",
                    element: <ForgotEmailSentPage />,
                  },
                  {
                    path: "/signup-email-sent",
                    element: <SignupEmailSentPage />,
                  },
                  { path: "/mfa-check", element: <MfaCheckPage /> },
                  {
                    path: "/reset-password-success",
                    element: <ResetPasswordSuccessPage />,
                  },
                ],
              },
              {
                path: "/oidc",
                element: <OidcLayout />,
                children: [
                  { index: true, element: <OidcIndexPage /> },
                  { path: "login", element: <OidcLoginPage /> },
                  { path: "permission", element: <OidcPermissionPage /> },
                  { path: "error", element: <OidcErrorPage /> },
                  {
                    path: "email-sent-confirmation",
                    element: <OidcEmailSentConfirmationPage />,
                  },
                ],
              },
            ],
          },

          // ── Protected routes (authenticated only) ──
          {
            element: (
              <ProtectedGuard>
                <Outlet />
              </ProtectedGuard>
            ),
            children: [
              // ── Console group (no impersonation allowed) ──
              {
                element: (
                  <ImpersonationChecker>
                    <ImpersonationTerminator>
                      <ConsoleLayout>
                        <Outlet />
                      </ConsoleLayout>
                    </ImpersonationTerminator>
                  </ImpersonationChecker>
                ),
                children: [
                  { path: "/console", element: <ConsolePage /> },
                  {
                    path: "/create-project",
                    element: <CreateProjectWrapper />,
                  },
                  { path: "/profile", element: <ProfilePage /> },
                ],
              },
              {
                path: "/project-overview",
                element: <ProjectOverviewLayout />,
                children: [
                  {
                    path: "environments",
                    element: <EnvironmentsPage />,
                  },
                ],
              },

              // ── Dashboard group (impersonation synchronized) ──
              {
                element: (
                  <ImpersonationChecker>
                    <ImpersonationSynchronizer>
                      <DashboardLayout />
                    </ImpersonationSynchronizer>
                  </ImpersonationChecker>
                ),

                children: [
                  { path: "/services/iam", element: <IamPage /> },
                  {
                    path: "/services/iam/user-detail/:id",
                    element: <IamUserDetailPage />,
                  },
                  {
                    path: "/services/iam/role-detail/:id",
                    element: <IamRoleDetailPage />,
                  },
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
                  {
                    path: "/services/iam/configure",
                    element: <IamConfigurePage />,
                  },
                  {
                    path: "/services/authentication/users",
                    element: <AuthenticationConfigPage section="users" />,
                  },
                  {
                    path: "/services/authentication/organizations",
                    element: (
                      <AuthenticationConfigPage section="organizations" />
                    ),
                  },
                  {
                    path: "/services/authentication/client-credential",
                    element: (
                      <AuthenticationConfigPage section="client-credential" />
                    ),
                  },
                  {
                    path: "/services/authentication/sso-configuration",
                    element: <SsoConfigurationPage />,
                  },
                  {
                    path: "/services/authentication/logs",
                    element: <AuthLogsPage />,
                  },
                  { path: "/services/mfa/logs", element: <MfaLogsPage /> },
                  {
                    path: "/services/rate-limiter",
                    element: <RateLimiterPage />,
                  },
                  {
                    path: "/managed-services",
                    element: <ManagedServicesPage />,
                  },
                  {
                    path: "/services/captcha/logs",
                    element: <CaptchaLogsPage />,
                  },
                  { path: "/dashboard", element: <DashboardOverview /> },
                  // { path: "/project-overview", element: <Navigate to="/project-overview/environments" replace /> },
                  // { path: "/project-overview/environments", element: <EnvironmentsPage /> },
                ],
              },
            ],
          },
          // ── Catch-all ──
          { path: "*", element: <Navigate to="/console" replace /> },
        ],
      },
    ],
  },
]);
