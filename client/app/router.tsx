import { createBrowserRouter, Navigate, Outlet } from "react-router";

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

// Device flow routes (RFC 8628)
import DeviceEntryRoute from "./routes/device";
import DeviceSuccessRoute from "./routes/device/success";

// Dashboard routes (protected)
import AuthLogsPage from "./routes/dashboard/auth-logs";
import AuthenticationConfigPage from "./routes/dashboard/authentication-config";
import CaptchaLogsPage from "./routes/dashboard/captcha-logs";
import IamPage from "./routes/dashboard/iam";
import IamAddPermissionPage from "./routes/dashboard/iam-add-permission";
import IamConfigurePage from "./routes/dashboard/iam-configure";
import IamLogsPage from "./routes/dashboard/iam-logs";
import IamPermissionDetailPage from "./routes/dashboard/iam-permission-detail";
import IamRoleDetailPage from "./routes/dashboard/iam-role-detail";
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
} from "@seliseblocks/genesis-os";
import { navigationMenus } from "./constants/navigation-menus";

const redirectPaths: Record<string, string> = {
  "/app/role-detail/*": "/app/iam?tab=roles",
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
          { path: "signup/:tenantId", element: <SignupPage /> },
          { path: "mfa-check", element: <MfaCheckPage /> },
          { path: ":provider/callback/:tenantId", element: <SSOCallbackPage  /> },
        ],
      },

      // ── Device authorization flow (RFC 8628) ──
      {
        path: "/device",
        element: <OidcLayout />,
        children: [
          { path: ":tenantId", element: <DeviceEntryRoute /> },
          { path: ":tenantId/success", element: <DeviceSuccessRoute /> },
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

      // ── Public confirmation pages (outside AuthResolver) ──
      // AuthResolver calls GET /api/auth/me on mount. When the session is
      // expired/revoked, blocks-kit HttpClient auto-redirects to /login for
      // any path not in excludedPaths (only /login, /signup). These pages
      // must stay outside AuthResolver so users can read the success message.
      {
        element: <PublicLayout />,
        children: [
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
                  // { path: "/signup", element: <SignupPage /> },
                  { path: "/sso-activate", element: <SsoActivatePage /> },
                ],
              },
              // {
              //   element: <PublicLayout />,
              //   children: [
              //     // { path: "/activate", element: <ActivatePage /> },

              //     // { path: "/forgot-password", element: <ForgotPasswordPage /> },

              //     // { path: "/resetpassword", element: <ResetPasswordPage /> },
              //     {
              //       path: "/activate-success",
              //       element: <ActivateSuccessPage />,
              //     },
              //     {
              //       path: "/forgot-email-sent",
              //       element: <ForgotEmailSentPage />,
              //     },
              //     {
              //       path: "/signup-email-sent",
              //       element: <SignupEmailSentPage />,
              //     },
              //     { path: "/mfa-check", element: <MfaCheckPage /> },
              //     {
              //       path: "/reset-password-success",
              //       element: <ResetPasswordSuccessPage />,
              //     },
              //   ],
              // },
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
                  { path: "iam/logs", element: <IamLogsPage /> },
                  {
                    path: "iam/configure",
                    element: <IamConfigurePage />,
                  },
                  // Users and Organizations moved to the OS frontend under
                  // /app/iam/*. This path stays registered because it is an
                  // existing redirect target, and now serves the one section
                  // this frontend still owns.
                  {
                    path: "authentication",
                    element: (
                      <AuthenticationConfigPage section="client-credential" />
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
