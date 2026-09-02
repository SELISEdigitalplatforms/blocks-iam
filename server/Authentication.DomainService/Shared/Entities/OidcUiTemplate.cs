using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;

namespace Authentication.DomainService.Entities
{
    /// <summary>
    /// Tenant-level copy and branding used by the public OIDC user interface.
    /// Properties remain nullable for BSON compatibility with older documents. Runtime
    /// reads return the stored document unchanged; validated writes require the complete shape.
    /// </summary>
    [BsonIgnoreExtraElements]
    public sealed class OidcUiTemplate
    {
        public const int CurrentSchemaVersion = 2;

        /// <summary>
        /// Identifies the persisted template value for mutation responses. This is storage
        /// metadata and is deliberately excluded from both public and management GET payloads.
        /// </summary>
        [JsonIgnore]
        public string? ItemId { get; set; }

        [JsonIgnore]
        public int SchemaVersion { get; set; }

        public OidcUiTemplateBranding? Branding { get; set; }
        public OidcUiTemplateTheme? Theme { get; set; }
        public OidcUiTemplatePages? Pages { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiTemplateBranding
    {
        public string? LogoUrl { get; set; }
        public string? BrandName { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiTemplateTheme
    {
        public OidcUiThemePalette? Light { get; set; }
        public OidcUiThemePalette? Dark { get; set; }

        // Transitional read-only compatibility for documents written before the
        // light/dark palette split. New API payloads and writes use Light/Dark.
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Primary { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Secondary { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Background { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Surface { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Text { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? MutedText { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Success { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Danger { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? Border { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? BorderStrong { get; set; }
        [JsonIgnore, BsonIgnoreIfNull]
        public string? AccentSoft { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiThemePalette
    {
        public string? Primary { get; set; }
        public string? Secondary { get; set; }
        public string? Background { get; set; }
        public string? Surface { get; set; }
        public string? Text { get; set; }
        public string? MutedText { get; set; }
        public string? Success { get; set; }
        public string? Danger { get; set; }
        public string? Border { get; set; }
        public string? BorderStrong { get; set; }
        public string? AccentSoft { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiTemplatePages
    {
        public OidcUiLoginPage? Login { get; set; }
        public OidcUiSignupPage? Signup { get; set; }
        public OidcUiForgotPasswordPage? ForgotPassword { get; set; }
        public OidcUiResetPasswordPage? ResetPassword { get; set; }
        public OidcUiActivationPage? Activation { get; set; }
        public OidcUiMfaPage? Mfa { get; set; }
        public OidcUiAccountSelectorPage? AccountSelector { get; set; }
        public OidcUiSharedPage? Shared { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiLoginPage
    {
        public string? Heading { get; set; }
        public string? EmailLabel { get; set; }
        public string? PasswordLabel { get; set; }
        public string? ForgotPasswordLink { get; set; }
        public string? SubmitButton { get; set; }
        public string? SignupPrompt { get; set; }
        public string? SignupLink { get; set; }
        public string? ActivationErrorTitle { get; set; }
        public string? ActivationErrorMessage { get; set; }
        public string? ActivateAccountButton { get; set; }
        public string? BackToLoginButton { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiSignupPage
    {
        public string? Heading { get; set; }
        public string? FirstNameLabel { get; set; }
        public string? LastNameLabel { get; set; }
        public string? EmailLabel { get; set; }
        public string? SubmitButton { get; set; }
        public string? TermsPrefix { get; set; }
        public string? TermsLinkText { get; set; }
        public string? PrivacyLinkText { get; set; }
        public string? LoginPrompt { get; set; }
        public string? LoginLink { get; set; }
        public string? SuccessTitle { get; set; }
        public string? SuccessSubtitle { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiForgotPasswordPage
    {
        public string? Heading { get; set; }
        public string? EmailLabel { get; set; }
        public string? SubmitButton { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiResetPasswordPage
    {
        public string? Heading { get; set; }
        public string? PasswordLabel { get; set; }
        public string? ConfirmPasswordLabel { get; set; }
        public string? LogoutFromDevicesLabel { get; set; }
        public string? SubmitButton { get; set; }
        public string? SuccessTitle { get; set; }
        public string? SuccessSubtitle { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiActivationPage
    {
        public string? Heading { get; set; }
        public string? PasswordLabel { get; set; }
        public string? ConfirmPasswordLabel { get; set; }
        public string? SubmitButton { get; set; }
        public string? SuccessTitle { get; set; }
        public string? SuccessSubtitle { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiMfaPage
    {
        public string? Heading { get; set; }
        public string? SubmitButton { get; set; }
        public string? ResendButton { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiAccountSelectorPage
    {
        public string? Heading { get; set; }
        public string? Subheading { get; set; }
    }

    [BsonIgnoreExtraElements]
    public sealed class OidcUiSharedPage
    {
        public string? FooterText { get; set; }
    }
}
