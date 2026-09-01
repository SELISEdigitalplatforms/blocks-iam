import type { IOidcUiTemplate } from "@blocks-idp/authentication/models/oidc-ui-template";

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
