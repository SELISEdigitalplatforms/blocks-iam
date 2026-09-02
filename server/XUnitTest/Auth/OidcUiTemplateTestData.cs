using Authentication.DomainService.Entities;
using Authentication.DomainService.Shared.RequestModel;

namespace XUnitTest.Auth
{
    internal static class OidcUiTemplateTestData
    {
        public static SaveOidcUiTemplateRequest ValidRequest()
        {
            var template = ValidTemplate();
            return new SaveOidcUiTemplateRequest
            {
                Branding = template.Branding,
                Theme = template.Theme,
                Pages = template.Pages
            };
        }

        public static OidcUiTemplate ValidTemplate() => new()
        {
            ItemId = "test-template-id",
            SchemaVersion = OidcUiTemplate.CurrentSchemaVersion,
            Branding = new OidcUiTemplateBranding { LogoUrl = null, BrandName = "Test IAM" },
            Theme = new OidcUiTemplateTheme
            {
                Light = Palette("#1266aa", "#2288cc", "#f7f8fa", "#ffffff", "#101828", "#667085", "#16a34a", "#dc2626", "#d0d5dd"),
                Dark = Palette("#3388cc", "#22bbee", "#080b14", "#101522", "#f2f4f7", "#98a2b3", "#22c55e", "#f87171", "#273142")
            },
            Pages = new OidcUiTemplatePages
            {
                Login = new OidcUiLoginPage
                {
                    Heading = "Test login", EmailLabel = "Email", PasswordLabel = "Password",
                    ForgotPasswordLink = "Forgot?", SubmitButton = "Sign in", SignupPrompt = "New here?",
                    SignupLink = "Create account", ActivationErrorTitle = "Activation required",
                    ActivationErrorMessage = "Activate your account first.", ActivateAccountButton = "Activate",
                    BackToLoginButton = "Back"
                },
                Signup = new OidcUiSignupPage
                {
                    Heading = "Test signup", FirstNameLabel = "First name", LastNameLabel = "Last name",
                    EmailLabel = "Email", SubmitButton = "Create account", TermsPrefix = "I accept",
                    TermsLinkText = "Terms", PrivacyLinkText = "Privacy", LoginPrompt = "Have an account?",
                    LoginLink = "Sign in", SuccessTitle = "Created", SuccessSubtitle = "Check your email"
                },
                ForgotPassword = new OidcUiForgotPasswordPage { Heading = "Recover", EmailLabel = "Email", SubmitButton = "Send" },
                ResetPassword = new OidcUiResetPasswordPage
                {
                    Heading = "Reset", PasswordLabel = "New password", ConfirmPasswordLabel = "Confirm password",
                    LogoutFromDevicesLabel = "Log out devices", SubmitButton = "Save", SuccessTitle = "Updated",
                    SuccessSubtitle = "Password updated"
                },
                Activation = new OidcUiActivationPage
                {
                    Heading = "Activate", PasswordLabel = "Password", ConfirmPasswordLabel = "Confirm password",
                    SubmitButton = "Activate", SuccessTitle = "Activated", SuccessSubtitle = "Account ready"
                },
                Mfa = new OidcUiMfaPage { Heading = "Verify", SubmitButton = "Verify", ResendButton = "Resend" },
                AccountSelector = new OidcUiAccountSelectorPage { Heading = "Accounts", Subheading = "Choose one" },
                Shared = new OidcUiSharedPage { FooterText = "Test footer {year}" }
            }
        };

        private static OidcUiThemePalette Palette(
            string primary,
            string secondary,
            string background,
            string surface,
            string text,
            string mutedText,
            string success,
            string danger,
            string border) => new()
        {
            Primary = primary,
            Secondary = secondary,
            Background = background,
            Surface = surface,
            Text = text,
            MutedText = mutedText,
            Success = success,
            Danger = danger,
            Border = border,
            BorderStrong = "rgba(18, 102, 170, 0.4)",
            AccentSoft = "rgba(18, 102, 170, 0.1)"
        };
    }
}
