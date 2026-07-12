import { createBrowserRouter, Navigate, Outlet } from "react-router-dom";

import { AuthLayout } from "./layouts/auth-layout";
import { OidcLayout } from "./layouts/oidc-layout";
import { PublicLayout } from "./layouts/public-layout";

// Auth routes (public, with auth layout)
import SignupPage from "./routes/auth/signup";
import SsoActivatePage from "./routes/auth/sso-activate";
import SSOCallbackPage from "./routes/auth/sso-callback";

// Public routes
import ActivatePage from "./routes/auth/activate";
import ActivateSuccessPage from "./routes/auth/activate-success";
import ForgotEmailSentPage from "./routes/auth/forgot-email-sent";
import ForgotPasswordPage from "./routes/auth/forgot-password";
import MfaCheckPage from "./routes/auth/mfa-check";
import ResetPasswordSuccessPage from "./routes/auth/reset-password-success";
import ResetPasswordPage from "./routes/auth/resetpassword";
import SignupEmailSentPage from "./routes/auth/signup-email-sent";

// OIDC routes (un-guarded)
import OidcErrorPage from "./routes/oidc/error";
import OidcIndexPage from "./routes/oidc/index";
import OidcLoginPage from "./routes/oidc/login";
import OidcPermissionPage from "./routes/oidc/permission";

// Dashboard routes (protected)
import AuthLogsPage from "./routes/dashboard/auth-logs";
import AuthenticationConfigPage from "./routes/dashboard/authentication-config";
import CaptchaLogsPage from "./routes/dashboard/captcha-logs";
import IamPage from "./routes/dashboard/iam";
import IamAddPermissionPage from "./routes/dashboard/iam-add-permission";
import IamConfigurePage from "./routes/dashboard/iam-configure";
import IamLogsPage from "./routes/dashboard/iam-logs";
import IamOrgDetailPage from "./routes/dashboard/iam-org-detail";
import IamPermissionDetailPage from "./routes/dashboard/iam-permission-detail";
import IamRoleDetailPage from "./routes/dashboard/iam-role-detail";
import IamUserDetailPage from "./routes/dashboard/iam-user-detail";
import ManagedServicesPage from "./routes/dashboard/managed-services";
import MfaLogsPage from "./routes/dashboard/mfa-logs";
import ProfilePage from "./routes/dashboard/profile";
import RateLimiterPage from "./routes/dashboard/rate-limiter";
import SsoConfigurationPage from "./routes/dashboard/sso-configuration";

import { CreateProjectWrapper } from "./pages/create-project/create-project";

import {
  AuthResolver,
  CallbackPage,
  ConsoleLayout,
  ConsolePage,
  DashboardOverview,
  DashboardRoute,
  LoginPage,
  ProtectedGuard,
  PublicGuard,
  TooltipProvider,
} from "@seliseblocks/blocks-kit";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/app/user-detail/*": "/app/iam",
  "/app/role-detail/*": "/app/iam?tab=roles",
  "/app/organization-detail/*": "/app/authentication/organizations",
  "/app/permission-detail/*": "/app/iam?tab=permissions",
  "/app/sso-configuration": "/app/authentication?tab=social",
};

export const router = createBrowserRouter([
  {
    element: <Outlet />,
    children: [
      // ── Callbacks outside AuthResolver ──
      {
        path: "/oidc",
        element: <OidcLayout />,
        children: [
          { index: true, element: <OidcIndexPage /> },
          { path: "login", element: <OidcLoginPage /> },
          { path: "permission", element: <OidcPermissionPage /> },
          { path: "error", element: <OidcErrorPage /> },
          
          // {
          //   path: "email-sent-confirmation",
          //   element: <OidcEmailSentConfirmationPage />,
          // },

          // OIDC-scoped auth pages (relative paths under /oidc)
          { path: "forgot-password", element: <ForgotPasswordPage /> },
          {
            path: "forgot-email-sent",
            element: <ForgotEmailSentPage />,
          },
          { path: "recover/:tenantId", element: <ResetPasswordPage /> },
          { path: "activate/:tenantId", element: <ActivatePage /> },
          { path: "mfa-check", element: <MfaCheckPage /> },
          { path: ":provider/callback/:tenantId", element: <SSOCallbackPage  /> },
        ],
      },
      {
        element: <Outlet />,
        children: [
          {
            path: "/login/callback",
            element: <CallbackPage defaultRedirectUrl="/app/console" />,
          },
          // { path: "/sso/:provider/callback", element: <SSOCallbackPage /> },
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

          // ── Public routes (unauthenticated only) ──
          {
            element: (
              <PublicGuard>
                <Outlet />
              </PublicGuard>
            ),

            children: [
              { path: "login", element: <LoginPage /> },

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
                  // { path: "/activate", element: <ActivatePage /> },

                  // { path: "/forgot-password", element: <ForgotPasswordPage /> },

                  // { path: "/resetpassword", element: <ResetPasswordPage /> },
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
            ],
          },

          // ── Protected routes (authenticated only) ──
          {
            path: "/app",
            element: (
              <ProtectedGuard>
                <Outlet />
              </ProtectedGuard>
            ),
            children: [
              { index: true, element: <Navigate to="console" replace /> },
              // ── Console group (no impersonation allowed) ──
              {
                element: (
                  <TooltipProvider delayDuration={0}>
                    <ConsoleLayout>
                      <Outlet />
                    </ConsoleLayout>
                  </TooltipProvider>
                ),
                children: [
                  { path: "console", element: <ConsolePage /> },
                  {
                    path: "create-project",
                    element: <CreateProjectWrapper />,
                  },
                  { path: "profile", element: <ProfilePage /> },
                ],
              },
              // ── Project overview group (impersonation terminated) ──
              // {
              //   path: "project/:tenantGroupId",
              //   element: (
              //     <ProjectOverviewRoute
              //       redirectPaths={redirectPaths}
              //       navigationMenus={navigationMenus}
              //     />
              //   ),
              //   children: [
              //     { index: true, element: <Navigate to="environments" replace /> },
              //     {
              //       path: "environments",
              //       element: <EnvironmentsPage />,
              //     },
              //   ],
              // },
              // ── Dashboard group (impersonation synchronized) ──
              {
                path: ":itemId",
                element: (
                  <DashboardRoute
                    redirectPaths={redirectPaths}
                    navigationMenus={navigationMenus}
                  />
                ),

                children: [
                  { index: true, element: <Navigate to="dashboard" replace /> },
                  { path: "iam", element: <IamPage /> },
                  {
                    path: "user-detail/:id",
                    element: <IamUserDetailPage />,
                  },
                  {
                    path: "role-detail/:id",
                    element: <IamRoleDetailPage />,
                  },
                  {
                    path: "permission-detail/new",
                    element: <IamAddPermissionPage />,
                  },
                  {
                    path: "permission-detail/:id",
                    element: <IamPermissionDetailPage />,
                  },
                  {
                    path: "organization-detail/:orgId",
                    element: <IamOrgDetailPage />,
                  },
                  { path: "iam/logs", element: <IamLogsPage /> },
                  {
                    path: "iam/configure",
                    element: <IamConfigurePage />,
                  },
                  {
                    path: "authentication",
                    element: <AuthenticationConfigPage section="users" />,
                  },
                  {
                    path: "users",
                    element: <AuthenticationConfigPage section="users" />,
                  },
                  {
                    path: "organizations",
                    element: (
                      <AuthenticationConfigPage section="organizations" />
                    ),
                  },
                  {
                    path: "client-credential",
                    element: (
                      <AuthenticationConfigPage section="client-credential" />
                    ),
                  },
                  {
                    path: "sso-configuration",
                    element: <SsoConfigurationPage />,
                  },
                  {
                    path: "authentication/logs",
                    element: <AuthLogsPage />,
                  },
                  {
                    path: "mfa/logs",
                    element: <MfaLogsPage />,
                  },
                  {
                    path: "rate-limiter",
                    element: <RateLimiterPage />,
                  },
                  {
                    path: "managed-services",
                    element: <ManagedServicesPage />,
                  },
                  {
                    path: "captcha/logs",
                    element: <CaptchaLogsPage />,
                  },
                  { path: "dashboard", element: <DashboardOverview /> },
                 
                ],
              },
            ],
          },
          // ── Catch-all ──
          { path: "*", element: <Navigate to="/app/console" replace /> },
        ],
      },
    ],
  },
]);
