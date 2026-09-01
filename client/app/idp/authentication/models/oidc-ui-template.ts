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
