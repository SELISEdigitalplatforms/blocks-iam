export interface IOidcUiThemePalette {
  primary: string; secondary: string; background: string; surface: string;
  text: string; mutedText: string; success: string; danger: string;
  border: string; borderStrong: string; accentSoft: string;
}

export interface IOidcUiTemplate {
  branding: { logoUrl: string | null; brandName: string };
  theme: {
    light: IOidcUiThemePalette;
    dark: IOidcUiThemePalette;
  };
  pages: {
    login: {
      heading: string; emailLabel: string; passwordLabel: string;
      forgotPasswordLink: string; submitButton: string; signupPrompt: string;
      signupLink: string; activationErrorTitle: string; activationErrorMessage: string;
      activateAccountButton: string; backToLoginButton: string;
    };
    signup: {
      heading: string; firstNameLabel: string; lastNameLabel: string; emailLabel: string;
      submitButton: string; termsPrefix: string; termsLinkText: string;
      privacyLinkText: string; loginPrompt: string; loginLink: string;
      successTitle: string; successSubtitle: string;
    };
    forgotPassword: { heading: string; emailLabel: string; submitButton: string };
    resetPassword: {
      heading: string; passwordLabel: string; confirmPasswordLabel: string;
      logoutFromDevicesLabel: string; submitButton: string;
      successTitle: string; successSubtitle: string;
    };
    activation: {
      heading: string; passwordLabel: string; confirmPasswordLabel: string;
      submitButton: string; successTitle: string; successSubtitle: string;
    };
    mfa: { heading: string; submitButton: string; resendButton: string | null };
    accountSelector: { heading: string; subheading: string };
    shared: { footerText: string };
  };
}

export const DEFAULT_OIDC_UI_TEMPLATE: IOidcUiTemplate = {
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
