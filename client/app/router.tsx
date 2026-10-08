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
import InvitationPage from "./routes/auth/invitation";
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
// The profile page is the only page this frontend serves to signed-in users;
// the console and IAM administration pages live in the OS frontend.
import ProfilePage from "./routes/dashboard/profile";

import {
  AuthResolver,
  CallbackPage,
  ConsoleLayout,
  LoginPage,
  ProtectedGuard,
  PublicGuard,
  TooltipProvider,
} from "@seliseblocks/genesis-os";

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

          // OIDC-scoped auth pages (relative paths under /oidc)
          { path: "forgot-password", element: <ForgotPasswordPage /> },
          {
            path: "forgot-email-sent",
            element: <ForgotEmailSentPage />,
          },
          { path: "recover/:tenantId", element: <ResetPasswordPage /> },
          { path: "activate/:tenantId", element: <ActivatePage /> },
          { path: "invitation/:tenantId", element: <InvitationPage /> },
          { path: "signup/:tenantId", element: <SignupPage /> },
          {
            path: "signup-email-sent",
            element: <SignupEmailSentPage />,
          },
          // OIDC-scoped confirmation pages: these keep the originating application's
          // clientId/redirect_uri in the URL so their "Log in" link can re-enter the
          // flow instead of dropping the user on the IAM root login.
          {
            path: "activate-success",
            element: <ActivateSuccessPage />,
          },
          {
            path: "reset-password-success",
            element: <ResetPasswordSuccessPage />,
          },
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
            element: <CallbackPage defaultRedirectUrl="/app/profile" />,
          },
          // The login launcher must remain reachable even when an IAM session already
          // exists. AuthResolver marks that session authenticated and PublicGuard then
          // redirects to /app/profile, so /login deliberately lives outside both.
          {
            path: "/login",
            element: <LoginPage />,
          },
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
              {
                element: <AuthLayout />,
                children: [
                  { path: "/sso-activate", element: <SsoActivatePage /> },
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
              { index: true, element: <Navigate to="profile" replace /> },
              // Profile is the only page this frontend serves; anything else typed
              // by hand falls through to the catch-all below and lands back on
              // /app/profile.
              {
                element: (
                  <TooltipProvider delayDuration={0}>
                    <ConsoleLayout>
                      <Outlet />
                    </ConsoleLayout>
                  </TooltipProvider>
                ),
                children: [
                  { path: "profile", element: <ProfilePage /> },
                ],
              },
              // Anything else under /app (typed by hand, stale bookmark, or an
              // old deep link from another service) goes back to the profile page.
              { path: "*", element: <Navigate to="/app/profile" replace /> },
            ],
          },
          // ── Catch-all ──
          { path: "*", element: <Navigate to="/app/profile" replace /> },
        ],
      },
    ],
  },
]);
