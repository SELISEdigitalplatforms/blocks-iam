using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using FluentAssertions;
using System.Reflection;

namespace XUnitTest.Auth
{
    public sealed class OidcUiTemplateTests
    {
        [Fact]
        public void CreateDefaultOidcUiTemplate_ReturnsEveryCurrentUiLiteral()
        {
            var expected = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding
                {
                    LogoUrl = null,
                    BrandName = "Blocks IAM"
                },
                Theme = new OidcUiTemplateTheme
                {
                    Primary = "#0066b2",
                    Secondary = "#00b2ff",
                    Background = "#050510",
                    Surface = "#0a0a1a",
                    Text = "#e8e8f0",
                    MutedText = "#5e5e7a",
                    Success = "#17a34a",
                    Danger = "#f87171",
                    Border = "#16162a",
                    BorderStrong = "rgba(0, 102, 178, 0.35)",
                    AccentSoft = "rgba(0, 102, 178, 0.10)"
                },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage
                    {
                        Heading = "Sign in to continue to your application",
                        EmailLabel = "Work Email",
                        PasswordLabel = "Password",
                        ForgotPasswordLink = "Forgot?",
                        SubmitButton = "Login",
                        SignupPrompt = "Not a member?",
                        SignupLink = "Create an account",
                        ActivationErrorTitle = "Account Not Verified",
                        ActivationErrorMessage = "Your account needs to be activated. Check your email for the activation link.",
                        ActivateAccountButton = "Activate Account",
                        BackToLoginButton = "Back to Login"
                    },
                    Signup = new OidcUiSignupPage
                    {
                        Heading = "Create Your Blocks Account",
                        FirstNameLabel = "First Name",
                        LastNameLabel = "Last Name",
                        EmailLabel = "Work Email",
                        SubmitButton = "Create Account",
                        TermsPrefix = "I agree to the",
                        TermsLinkText = "Terms of Service",
                        PrivacyLinkText = "Privacy Policy",
                        LoginPrompt = "Already a member?",
                        LoginLink = "Sign in",
                        SuccessTitle = "Account Created",
                        SuccessSubtitle = "Check your inbox for the activation link…"
                    },
                    ForgotPassword = new OidcUiForgotPasswordPage
                    {
                        Heading = "Reset Password",
                        EmailLabel = "Email",
                        SubmitButton = "Send Recovery Link"
                    },
                    ResetPassword = new OidcUiResetPasswordPage
                    {
                        Heading = "Set a new password",
                        PasswordLabel = "New Password",
                        ConfirmPasswordLabel = "Confirm Password",
                        LogoutFromDevicesLabel = "Logout from all devices",
                        SubmitButton = "Set Password",
                        SuccessTitle = "Password Updated",
                        SuccessSubtitle = "Your password has been reset successfully."
                    },
                    Activation = new OidcUiActivationPage
                    {
                        Heading = "Activate Your Account",
                        PasswordLabel = "Password",
                        ConfirmPasswordLabel = "Confirm Password",
                        SubmitButton = "Activate",
                        SuccessTitle = "Account Activated",
                        SuccessSubtitle = "Your account is ready to use."
                    },
                    Mfa = new OidcUiMfaPage
                    {
                        Heading = "Verify it's you",
                        SubmitButton = "Verify",
                        ResendButton = "Resend Code"
                    },
                    AccountSelector = new OidcUiAccountSelectorPage
                    {
                        Heading = "Blocks IAM",
                        Subheading = "Select Account"
                    },
                    Shared = new OidcUiSharedPage
                    {
                        FooterText = "© {year} SELISE Digital Platforms. All rights reserved."
                    }
                }
            };

            IdpService.CreateDefaultOidcUiTemplate().Should().BeEquivalentTo(expected);
        }

        [Fact]
        public void CreateDefaultOidcUiTemplate_ReturnsIndependentObjects()
        {
            var first = IdpService.CreateDefaultOidcUiTemplate();
            var second = IdpService.CreateDefaultOidcUiTemplate();

            first.Branding!.BrandName = "changed";

            second.Branding!.BrandName.Should().Be("Blocks IAM");
            first.Should().NotBeSameAs(second);
        }

        [Fact]
        public void MergeOidcUiTemplateWithDefaults_NoSavedDocument_ReturnsFullDefault()
        {
            var merged = IdpService.MergeOidcUiTemplateWithDefaults(null);

            merged.Should().BeEquivalentTo(IdpService.CreateDefaultOidcUiTemplate());
        }

        [Fact]
        public void MergeOidcUiTemplateWithDefaults_PartialDocument_MergesPerLeaf()
        {
            var saved = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding
                {
                    BrandName = "Acme Corp"
                },
                Theme = new OidcUiTemplateTheme
                {
                    Primary = "#ff0000",
                    Border = null
                },
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage
                    {
                        Heading = "Acme login",
                        EmailLabel = string.Empty
                    }
                }
            };

            var merged = IdpService.MergeOidcUiTemplateWithDefaults(saved);

            merged.Branding!.BrandName.Should().Be("Acme Corp");
            merged.Branding.LogoUrl.Should().BeNull();
            merged.Theme!.Primary.Should().Be("#ff0000");
            merged.Theme.Border.Should().Be("#16162a");
            merged.Theme.Secondary.Should().Be("#00b2ff");
            merged.Pages!.Login!.Heading.Should().Be("Acme login");
            merged.Pages.Login.EmailLabel.Should().BeEmpty("only null values are default-filled");
            merged.Pages.Signup!.Heading.Should().Be("Create Your Blocks Account");
        }

        [Fact]
        public void MergeOidcUiTemplateWithDefaults_CorruptRequiredLeaf_UsesDefault()
        {
            var saved = new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding { BrandName = null }
            };

            var merged = IdpService.MergeOidcUiTemplateWithDefaults(saved);

            merged.Branding!.BrandName.Should().Be("Blocks IAM");
        }

        [Fact]
        public void MergeOidcUiTemplateWithDefaults_AllNullLeaves_UsesEveryDefault()
        {
            var saved = EmptyStructuredTemplate();

            var merged = IdpService.MergeOidcUiTemplateWithDefaults(saved);

            merged.Should().BeEquivalentTo(IdpService.CreateDefaultOidcUiTemplate());
        }

        [Fact]
        public void MergeOidcUiTemplateWithDefaults_FullyCustomizedDocument_UsesEverySavedLeaf()
        {
            var saved = IdpService.CreateDefaultOidcUiTemplate();
            CustomizeEveryStringLeaf(saved, "template");

            var merged = IdpService.MergeOidcUiTemplateWithDefaults(saved);

            merged.Should().BeEquivalentTo(saved);
            merged.Should().NotBeSameAs(saved);
        }

        private static OidcUiTemplate EmptyStructuredTemplate()
        {
            return new OidcUiTemplate
            {
                Branding = new OidcUiTemplateBranding(),
                Theme = new OidcUiTemplateTheme(),
                Pages = new OidcUiTemplatePages
                {
                    Login = new OidcUiLoginPage(),
                    Signup = new OidcUiSignupPage(),
                    ForgotPassword = new OidcUiForgotPasswordPage(),
                    ResetPassword = new OidcUiResetPasswordPage(),
                    Activation = new OidcUiActivationPage(),
                    Mfa = new OidcUiMfaPage(),
                    AccountSelector = new OidcUiAccountSelectorPage(),
                    Shared = new OidcUiSharedPage()
                }
            };
        }

        private static void CustomizeEveryStringLeaf(object value, string path)
        {
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.PropertyType == typeof(string))
                {
                    property.SetValue(value, $"custom:{path}.{property.Name}");
                    continue;
                }

                var nested = property.GetValue(value);
                if (nested is not null)
                {
                    CustomizeEveryStringLeaf(nested, $"{path}.{property.Name}");
                }
            }
        }
    }
}
