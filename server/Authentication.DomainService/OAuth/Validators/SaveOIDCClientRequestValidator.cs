using Authentication.DomainService.Entities;
using Authentication.DomainService.RequestModel;
using Authentication.DomainService.Utilities;
using FluentValidation;

namespace Authentication.DomainService.OAuth
{
    public sealed class SaveOIDCClientRequestValidator : AbstractValidator<SaveOIDCClientRequest>
    {
        private const string RedirectUrisMessage =
            "Each redirect URI must be an http(s) URL or a mobile app URI whose scheme is a reverse domain name, such as com.example.app://callback.";
        private const string PostLogoutRedirectUrisMessage =
            "Each post-logout redirect URI must be an http(s) URL or a mobile app URI whose scheme is a reverse domain name, such as com.example.app://callback.";
        private const string AppSchemeClientTypeMessage = "Mobile app redirect URIs require a public client.";
        private const string AppSchemePkceMessage = "Mobile app redirect URIs require PKCE.";
        private const string AppSchemeIdentityProviderMessage =
            "A client with mobile app redirect URIs cannot be registered as an identity provider.";
        public const string DeviceFlowResponseTypesMessage =
            "A device-flow client must have no response types (allowedResponseTypes: []).";

        public SaveOIDCClientRequestValidator()
        {
            RuleFor(x => x.ItemId)
                .MaximumLength(128)
                .Must(BeUrlSafe)
                .When(x => !string.IsNullOrWhiteSpace(x.ItemId));

            RuleFor(x => x.ClientType)
                .Must(BeOneOfPublicOrConfidential)
                .When(x => !string.IsNullOrWhiteSpace(x.ClientType));

            // Native apps (RFC 8252) cannot hold a secret and receive the code through an OS-level
            // scheme handler, so an app-scheme URI is only allowed on a public client with PKCE.
            // Federation is refused too: the IdentityProvider record would copy these URIs, and
            // its own validator accepts http(s) only. Device-flow clients have no redirect URIs
            // once saved, so none of this applies to them.
            RuleFor(x => x.ClientType)
                .Must(value => string.Equals(value, "public", StringComparison.OrdinalIgnoreCase))
                .When(HasAppSchemeUri)
                .WithMessage(AppSchemeClientTypeMessage);

            RuleFor(x => x.RequirePkce)
                .Must(value => value)
                .When(HasAppSchemeUri)
                .WithMessage(AppSchemePkceMessage);

            RuleFor(x => x.RegisterAsIdentityProvider)
                .Must(value => !value)
                .When(HasAppSchemeUri)
                .WithMessage(AppSchemeIdentityProviderMessage);

            RuleFor(x => x.RedirectUris)
                .Must(AllValidUris)
                .When(x => x.RedirectUris != null && x.RedirectUris.Count > 0)
                .WithMessage(RedirectUrisMessage);

            RuleFor(x => x.AllowedResponseTypes)
                .Must(ContainCode)
                .When(x => x.AllowedResponseTypes != null && x.AllowedResponseTypes.Count > 0);

            RuleFor(x => x.ExternalDiscoveryEndpoint)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.ExternalDiscoveryEndpoint));

            RuleFor(x => x.FrontChannelLogoutUri)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.FrontChannelLogoutUri));

            RuleFor(x => x.BackChannelLogoutUri)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.BackChannelLogoutUri));

            RuleFor(x => x.PostLogoutRedirectUris)
                .Must(AllValidUris)
                .When(x => x.PostLogoutRedirectUris != null && x.PostLogoutRedirectUris.Count > 0)
                .WithMessage(PostLogoutRedirectUrisMessage);

            RuleFor(x => x.AllowedScopes)
                .Must(AllNonEmpty)
                .When(x => x.AllowedScopes != null && x.AllowedScopes.Count > 0);

            // Bound to AllowedResponseTypes with its own message: as a whole-object rule it failed
            // with "The specified condition was not met for ''", and since AllowedResponseTypes
            // defaults to ["code"] every device client registered without it hit that.
            RuleFor(x => x.AllowedResponseTypes)
                .Must(NotContainCodeResponseType)
                .When(x => x.IsDeviceFlowClient)
                .WithMessage(DeviceFlowResponseTypesMessage);

            // Optional per-client token lifetimes. Null fields mean "use the project value".
            RuleFor(x => x.TokenLifetimes!.AccessTokenValidForNumberMinutes)
                .Must(value => OidcClientTokenLifetimes.IsValidAccessMinutes(value!.Value))
                .When(x => x.TokenLifetimes?.AccessTokenValidForNumberMinutes != null)
                .WithMessage(AccessTokenLifetimeMessage);

            RuleFor(x => x.TokenLifetimes!.RefreshTokenValidForNumberMinutes)
                .Must(value => OidcClientTokenLifetimes.IsValidRefreshMinutes(value!.Value))
                .When(x => x.TokenLifetimes?.RefreshTokenValidForNumberMinutes != null)
                .WithMessage(RefreshTokenLifetimeMessage);

            RuleFor(x => x.TokenLifetimes!.AbsoluteRefreshTokenValidForNumberMinutes)
                .Must(value => OidcClientTokenLifetimes.IsValidRefreshMinutes(value!.Value))
                .When(x => x.TokenLifetimes?.AbsoluteRefreshTokenValidForNumberMinutes != null)
                .WithMessage(AbsoluteRefreshTokenLifetimeMessage);

            RuleFor(x => x.TokenLifetimes!.AbsoluteRefreshTokenValidForNumberMinutes)
                .Must((request, absolute) => absolute!.Value >= request.TokenLifetimes!.RefreshTokenValidForNumberMinutes!.Value)
                .When(x => x.TokenLifetimes?.AbsoluteRefreshTokenValidForNumberMinutes != null
                    && x.TokenLifetimes.RefreshTokenValidForNumberMinutes != null
                    && OidcClientTokenLifetimes.IsValidRefreshMinutes(x.TokenLifetimes.AbsoluteRefreshTokenValidForNumberMinutes.Value)
                    && OidcClientTokenLifetimes.IsValidRefreshMinutes(x.TokenLifetimes.RefreshTokenValidForNumberMinutes.Value))
                .WithMessage(AbsoluteBelowSlidingMessage);
        }

        public const string AccessTokenLifetimeMessage =
            "Access token lifetime must be between 1 and 1440 minutes.";
        public const string RefreshTokenLifetimeMessage =
            "Refresh token idle lifetime must be between 1 and 525600 minutes.";
        public const string AbsoluteRefreshTokenLifetimeMessage =
            "Refresh token maximum lifetime must be between 1 and 525600 minutes.";
        public const string AbsoluteBelowSlidingMessage =
            "Refresh token maximum lifetime must be at least the refresh token idle lifetime.";

        private static bool NotContainCodeResponseType(List<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return true;
            }

            return !values.Any(v => string.Equals(v, "code", StringComparison.OrdinalIgnoreCase));
        }

        private static bool BeUrlSafe(string value)
        {
            foreach (var c in value)
            {
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))
                    return false;
            }
            return true;
        }

        private static bool BeOneOfPublicOrConfidential(string value)
        {
            return string.Equals(value, "public", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "confidential", StringComparison.OrdinalIgnoreCase);
        }

        private static bool AllValidUris(List<string> values)
        {
            return values.All(v => RedirectUriSchemes.IsWebUri(v) || RedirectUriSchemes.IsAppScheme(v));
        }

        private static bool HasAppSchemeUri(SaveOIDCClientRequest request)
        {
            if (request.IsDeviceFlowClient)
            {
                return false;
            }

            return (request.RedirectUris?.Any(RedirectUriSchemes.IsAppScheme) ?? false)
                || (request.PostLogoutRedirectUris?.Any(RedirectUriSchemes.IsAppScheme) ?? false);
        }

        private static bool ContainCode(List<string> values)
        {
            return values.Any(v => string.Equals(v, "code", StringComparison.OrdinalIgnoreCase));
        }

        private static bool BeValidHttpsUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        private static bool AllNonEmpty(List<string> values)
        {
            return values.All(v => !string.IsNullOrWhiteSpace(v));
        }
    }
}
