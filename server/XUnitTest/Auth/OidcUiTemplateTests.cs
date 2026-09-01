using Authentication.DomainService.Authentication;
using Authentication.DomainService.Entities;
using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using MongoDB.Bson;

namespace XUnitTest.Auth
{
    public sealed class OidcUiTemplateTests
    {
        [Fact]
        public void CreateDefaultOidcUiTemplate_ReturnsEveryCurrentUiLiteral()
        {
            var expected = new OidcUiTemplate
            {
                SchemaVersion = OidcUiTemplate.CurrentSchemaVersion,
                Branding = new OidcUiTemplateBranding
                {
                    LogoUrl = null,
                    BrandName = "Blocks IAM"
                },
                Theme = new OidcUiTemplateTheme
                {
                    Light = new OidcUiThemePalette
                    {
                        Primary = "#0066b2",
                        Secondary = "#0084d4",
                        Background = "#f5f7fb",
                        Surface = "#ffffff",
                        Text = "#0c1024",
                        MutedText = "#5b6378",
                        Success = "#16a34a",
                        Danger = "#dc2626",
                        Border = "#dde2ec",
                        BorderStrong = "rgba(0, 102, 178, 0.45)",
                        AccentSoft = "rgba(0, 102, 178, 0.08)"
                    },
                    Dark = new OidcUiThemePalette
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
                    }
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
        public void ItemId_IsStorageMetadataAndNeverAppearsInTemplateJson()
        {
            var template = IdpService.CreateDefaultOidcUiTemplate();
            template.ItemId = "internal-id";

            var json = JsonSerializer.Serialize(template, new JsonSerializerOptions(JsonSerializerDefaults.Web));

            json.Should().NotContain("itemId");
            json.Should().NotContain("schemaVersion");
            json.Should().Contain("brandName");
            json.Should().Contain("\"light\"");
            json.Should().Contain("\"dark\"");
            json.Should().NotContain("\"primary\":null");
        }

        [Fact]
        public void CompleteTemplate_PersistsVersionedLightAndDarkPalettesWithoutLegacyFields()
        {
            var document = IdpService.CreateDefaultOidcUiTemplate().ToBsonDocument();
            var theme = document["Theme"].AsBsonDocument;

            document["SchemaVersion"].AsInt32.Should().Be(OidcUiTemplate.CurrentSchemaVersion);
            theme.Contains("Light").Should().BeTrue();
            theme.Contains("Dark").Should().BeTrue();
            theme.Contains("Primary").Should().BeFalse();
            theme["Light"].AsBsonDocument["Background"].AsString.Should().Be("#f5f7fb");
            theme["Dark"].AsBsonDocument["Background"].AsString.Should().Be("#050510");
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
            merged.Theme!.Dark!.Primary.Should().Be("#ff0000");
            merged.Theme.Dark.Border.Should().Be("#16162a");
            merged.Theme.Dark.Secondary.Should().Be("#00b2ff");
            merged.Theme.Light!.Background.Should().Be("#f5f7fb");
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
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
                {
                    continue;
                }

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
