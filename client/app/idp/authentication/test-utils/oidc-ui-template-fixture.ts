import type { IOidcUiTemplate } from "@blocks-idp/authentication/models/oidc-ui-template";

// The baseline template most tests render with. Its copy mirrors the app's
// built-in DEFAULT_OIDC_UI_TEMPLATE so tests that locate elements by that copy
// (e.g. a "Login" button) don't all need rewriting.
// Tests that specifically verify tenant customization override with
// OIDC_UI_TEMPLATE_FIXTURE below instead.
export const DEFAULT_OIDC_UI_TEMPLATE_FIXTURE: IOidcUiTemplate = {
  branding: { logoUrl: null, brandName: "Blocks IAM" },
  theme: {
    light: {
      primary: "#0066b2", secondary: "#0084d4", background: "#f5f7fb",
      surface: "#ffffff", text: "#0c1024", mutedText: "#5b6378",
      success: "#16a34a", danger: "#dc2626", border: "#dde2ec",
      borderStrong: "rgba(0, 102, 178, 0.45)", accentSoft: "rgba(0, 102, 178, 0.08)",
    },
    dark: {
      primary: "#0066b2", secondary: "#00b2ff", background: "#050510",
      surface: "#0a0a1a", text: "#e8e8f0", mutedText: "#5e5e7a",
      success: "#17a34a", danger: "#f87171", border: "#16162a",
      borderStrong: "rgba(0, 102, 178, 0.35)", accentSoft: "rgba(0, 102, 178, 0.10)",
    },
  },
  pages: {
    login: {
      heading: "Sign in to continue to your application", emailLabel: "Work Email",
      passwordLabel: "Password", forgotPasswordLink: "Forgot?", submitButton: "Login",
      signupPrompt: "Not a member?", signupLink: "Create an account",
      activationErrorTitle: "Account Not Verified",
      activationErrorMessage: "Your account needs to be activated. Check your email for the activation link.",
      activateAccountButton: "Activate Account", backToLoginButton: "Back to Login",
    },
    signup: {
      heading: "Create Your Blocks Account", firstNameLabel: "First Name",
      lastNameLabel: "Last Name", emailLabel: "Work Email", submitButton: "Create Account",
      termsPrefix: "I agree to the", termsLinkText: "Terms of Service",
      privacyLinkText: "Privacy Policy", loginPrompt: "Already a member?", loginLink: "Sign in",
      successTitle: "Account Created", successSubtitle: "Check your inbox for the activation link…",
    },
    forgotPassword: { heading: "Reset Password", emailLabel: "Email", submitButton: "Send Recovery Link" },
    resetPassword: {
      heading: "Set a new password", passwordLabel: "New Password",
      confirmPasswordLabel: "Confirm Password", logoutFromDevicesLabel: "Logout from all devices",
      submitButton: "Set Password", successTitle: "Password Updated",
      successSubtitle: "Your password has been reset successfully.",
    },
    activation: {
      heading: "Activate Your Account", passwordLabel: "Password",
      confirmPasswordLabel: "Confirm Password", submitButton: "Activate",
      successTitle: "Account Activated", successSubtitle: "Your account is ready to use.",
    },
    mfa: { heading: "Verify it's you", submitButton: "Verify", resendButton: "Resend Code" },
    accountSelector: { heading: "Blocks IAM", subheading: "Select Account" },
    shared: { footerText: "© {year} SELISE Digital Platforms. All rights reserved." },
  },
};

export const OIDC_UI_TEMPLATE_FIXTURE: IOidcUiTemplate = {
  branding: { logoUrl: null, brandName: "Test IAM" },
  theme: {
    light: {
      primary: "#1266aa", secondary: "#2288cc", background: "#f7f8fa",
      surface: "#ffffff", text: "#101828", mutedText: "#667085",
      success: "#16a34a", danger: "#dc2626", border: "#d0d5dd",
      borderStrong: "rgba(18, 102, 170, 0.4)", accentSoft: "rgba(18, 102, 170, 0.1)",
    },
    dark: {
      primary: "#3388cc", secondary: "#22bbee", background: "#080b14",
      surface: "#101522", text: "#f2f4f7", mutedText: "#98a2b3",
      success: "#22c55e", danger: "#f87171", border: "#273142",
      borderStrong: "rgba(51, 136, 204, 0.4)", accentSoft: "rgba(51, 136, 204, 0.1)",
    },
  },
  pages: {
    login: {
      heading: "Test login", emailLabel: "Email", passwordLabel: "Password",
      forgotPasswordLink: "Forgot?", submitButton: "Sign in", signupPrompt: "New here?",
      signupLink: "Create account", activationErrorTitle: "Activation required",
      activationErrorMessage: "Activate your account first.", activateAccountButton: "Activate",
      backToLoginButton: "Back",
    },
    signup: {
      heading: "Test signup", firstNameLabel: "First name", lastNameLabel: "Last name",
      emailLabel: "Email", submitButton: "Create account", termsPrefix: "I accept",
      termsLinkText: "Terms", privacyLinkText: "Privacy", loginPrompt: "Have an account?",
      loginLink: "Sign in", successTitle: "Created", successSubtitle: "Check your email",
    },
    forgotPassword: { heading: "Recover", emailLabel: "Email", submitButton: "Send" },
    resetPassword: {
      heading: "Reset", passwordLabel: "New password", confirmPasswordLabel: "Confirm password",
      logoutFromDevicesLabel: "Log out devices", submitButton: "Save", successTitle: "Updated",
      successSubtitle: "Password updated",
    },
    activation: {
      heading: "Activate", passwordLabel: "Password", confirmPasswordLabel: "Confirm password",
      submitButton: "Activate", successTitle: "Activated", successSubtitle: "Account ready",
    },
    mfa: { heading: "Verify", submitButton: "Verify", resendButton: "Resend" },
    accountSelector: { heading: "Accounts", subheading: "Choose one" },
    shared: { footerText: "Test footer {year}" },
  },
};
