export interface IOidcUiThemePalette {
  primary: string; secondary: string; background: string; surface: string;
  text: string; mutedText: string; success: string; danger: string;
  border: string; borderStrong: string; accentSoft: string;
}

export interface IOidcUiTemplate {
  branding: { logoUrl: string | null; brandName: string };
  theme: { light: IOidcUiThemePalette; dark: IOidcUiThemePalette };
  pages: {
    login: { heading: string; emailLabel: string; passwordLabel: string; forgotPasswordLink: string; submitButton: string; signupPrompt: string; signupLink: string; ssoSeparatorText: string; activationErrorTitle: string; activationErrorMessage: string; activateAccountButton: string; backToLoginButton: string };
    signup: { heading: string; firstNameLabel: string; lastNameLabel: string; emailLabel: string; organizationNameLabel: string; submitButton: string; creatingButton: string; termsPrefix: string; termsLinkText: string; privacyLinkText: string; termsConjunction: string; loginPrompt: string; loginLink: string; ssoSeparatorText: string; successTitle: string; successSubtitle: string; emailSentTitle: string; emailSentSubtitle: string; resendPromptTitle: string; resendPromptSubtitle: string; resendButton: string; loginSentPrompt: string; loginSentLink: string };
    forgotPassword: { heading: string; introText: string; emailLabel: string; submitButton: string; backToLoginButton: string; successTitle: string; successSubtitle: string; resendPromptTitle: string; resendPromptSubtitle: string; resendButton: string; loginPrompt: string; loginLink: string };
    resetPassword: { heading: string; passwordLabel: string; confirmPasswordLabel: string; logoutFromDevicesLabel: string; submitButton: string; resettingButton: string; missingCodeMessage: string; requestNewLinkButton: string; backToLoginButton: string; successTitle: string; successSubtitle: string; readyTitle: string; readySubtitle: string; loginButton: string };
    activation: { heading: string; firstNameLabel: string; lastNameLabel: string; passwordLabel: string; confirmPasswordLabel: string; submitButton: string; activatingButton: string; successTitle: string; successSubtitle: string; invalidHeading: string; invalidMessage: string; expiredHeading: string; expiredMessage: string; alreadyActiveHeading: string; alreadyActiveMessage: string; resendButton: string; resendSuccessMessage: string; resendFailureMessage: string; autoConfirmCaptchaText: string; autoConfirmProgressText: string; autoActivatingLabel: string; readyTitle: string; readyWithPasswordSubtitle: string; readySubtitle: string; loginButton: string; backToLoginButton: string };
    mfa: { heading: string; submitButton: string; resendButton: string | null };
    accountSelector: { heading: string; subheading: string | null; bodyText: string };
    shared: { footerText: string; helpPrompt: string; supportLinkText: string };
  };
}

export const DEFAULT_OIDC_UI_TEMPLATE: IOidcUiTemplate = {
  branding: { logoUrl: null, brandName: "Blocks IAM" },
  theme: {
    light: { primary: "#0066b2", secondary: "#0084d4", background: "#f5f7fb", surface: "#ffffff", text: "#0c1024", mutedText: "#5b6378", success: "#16a34a", danger: "#dc2626", border: "#dde2ec", borderStrong: "rgba(0, 102, 178, 0.45)", accentSoft: "rgba(0, 102, 178, 0.08)" },
    dark: { primary: "#0066b2", secondary: "#00b2ff", background: "#050510", surface: "#0a0a1a", text: "#e8e8f0", mutedText: "#5e5e7a", success: "#17a34a", danger: "#f87171", border: "#16162a", borderStrong: "rgba(0, 102, 178, 0.35)", accentSoft: "rgba(0, 102, 178, 0.10)" },
  },
  pages: {
    login: { heading: "Sign in to continue to your application", emailLabel: "Work Email", passwordLabel: "Password", forgotPasswordLink: "Forgot?", submitButton: "Login", signupPrompt: "Not a member?", signupLink: "Create an account", ssoSeparatorText: "or", activationErrorTitle: "Account Not Verified", activationErrorMessage: "Your account needs to be activated. Check your email for the activation link.", activateAccountButton: "Activate Account", backToLoginButton: "Back to Login" },
    signup: { heading: "Create Your Blocks Account", firstNameLabel: "First Name", lastNameLabel: "Last Name", emailLabel: "Work Email", organizationNameLabel: "Organization Name", submitButton: "Create Account", creatingButton: "Creating Account...", termsPrefix: "I agree to the", termsLinkText: "Terms of Service", privacyLinkText: "Privacy Policy", termsConjunction: "and the", loginPrompt: "Already a member?", loginLink: "Sign in", ssoSeparatorText: "or", successTitle: "Account Created", successSubtitle: "Check your inbox for the activation link...", emailSentTitle: "Email sent", emailSentSubtitle: "An email has been sent to {email}. Please follow the link in the email to continue your sign up.", resendPromptTitle: "Didn't receive the email?", resendPromptSubtitle: "Check your spam folder or resend the email.", resendButton: "Resend email", loginSentPrompt: "Already have an account?", loginSentLink: "Go to login" },
    forgotPassword: { heading: "Reset Password", introText: "Enter your email and we'll dispatch a recovery link.", emailLabel: "Email", submitButton: "Send Recovery Link", backToLoginButton: "Back to login", successTitle: "Email sent", successSubtitle: "A password reset email has been sent to {email}. Please follow the instructions in the email to reset your password.", resendPromptTitle: "Didn't receive the email?", resendPromptSubtitle: "Check your spam folder or resend the email.", resendButton: "Resend email", loginPrompt: "Remember your password?", loginLink: "Go to login" },
    resetPassword: { heading: "Set a new password", passwordLabel: "New Password", confirmPasswordLabel: "Confirm Password", logoutFromDevicesLabel: "Logout from all devices", submitButton: "Set Password", resettingButton: "Resetting...", missingCodeMessage: "The reset code is missing or invalid. Please request a new reset link.", requestNewLinkButton: "Request new reset link", backToLoginButton: "Back to login", successTitle: "Password Updated", successSubtitle: "Your password has been reset successfully.", readyTitle: "Ready to sign in?", readySubtitle: "Sign in with your new password.", loginButton: "Log in" },
    activation: { heading: "Activate Your Account", firstNameLabel: "First Name", lastNameLabel: "Last Name", passwordLabel: "Password", confirmPasswordLabel: "Confirm Password", submitButton: "Activate", activatingButton: "Activating...", successTitle: "Account Activated", successSubtitle: "Your account is ready to use.", invalidHeading: "Invalid Activation Link", invalidMessage: "The activation code is invalid. Please check the link or request a new activation email from your administrator.", expiredHeading: "Link Expired", expiredMessage: "This activation link has expired and can't be used anymore. Please request a new link to complete your account activation.", alreadyActiveHeading: "Already Activated", alreadyActiveMessage: "This account is already active, so there is nothing left to confirm. Sign in to continue -- use the forgot-password link if you have not set a password yet.", resendButton: "Resend activation link", resendSuccessMessage: "A new activation link has been sent to your email.", resendFailureMessage: "Failed to resend activation link. Please try again later.", autoConfirmCaptchaText: "Confirm you are not a robot to finish activating your account.", autoConfirmProgressText: "Confirming your activation link and setting up your account.", autoActivatingLabel: "Activating...", readyTitle: "Ready to sign in?", readyWithPasswordSubtitle: "Use your password to access your workspace.", readySubtitle: "Sign in to access your workspace.", loginButton: "Log in", backToLoginButton: "Back to login" },
    mfa: { heading: "Verify it's you", submitButton: "Verify", resendButton: "Resend Code" },
    accountSelector: { heading: "Blocks IAM", subheading: "Select Account", bodyText: "You have multiple accounts. Please select one to continue." },
    shared: { footerText: "(c) {year} SELISE Digital Platforms. All rights reserved.", helpPrompt: "Need help?", supportLinkText: "Contact support" },
  },
};

const mergeDefaults = (defaults: unknown, value: unknown): unknown => {
  if (value === undefined) return structuredClone(defaults);
  if (defaults !== null && value !== null && typeof defaults === "object" && typeof value === "object" && !Array.isArray(defaults) && !Array.isArray(value)) {
    return Object.fromEntries(Object.entries(defaults).map(([key, defaultValue]) => [key, mergeDefaults(defaultValue, (value as Record<string, unknown>)[key])]));
  }
  return value;
};

export const normalizeOidcUiTemplate = (template: IOidcUiTemplate): IOidcUiTemplate =>
  mergeDefaults(DEFAULT_OIDC_UI_TEMPLATE, template) as IOidcUiTemplate;
