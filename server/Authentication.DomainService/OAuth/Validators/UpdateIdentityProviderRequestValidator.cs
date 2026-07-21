using Authentication.DomainService.Shared.RequestModel;
using FluentValidation;

namespace Authentication.DomainService.OAuth
{
    public sealed class UpdateIdentityProviderRequestValidator : AbstractValidator<UpdateIdentityProviderRequest>
    {
        public UpdateIdentityProviderRequestValidator()
        {
            RuleFor(x => x.Provider)
                .NotEmpty()
                .When(x => x.Provider != null);

            RuleFor(x => x.ProviderType)
                .Must(BeInProviderTypes)
                .When(x => x.ProviderType != null);

            RuleFor(x => x.Protocol)
                .Must(BeInProtocols)
                .When(x => x.Protocol != null);

            RuleFor(x => x.ClientId)
                .NotEmpty()
                .When(x => x.ClientId != null);

            RuleFor(x => x.WellKnownUrl)
                .NotEmpty()
                .When(x => x.ProviderType == "blocks-oidc");

            RuleFor(x => x.WellKnownUrl)
                .Must(BeAValidUrl)
                .When(x => x.ProviderType == "blocks-oidc" && !string.IsNullOrWhiteSpace(x.WellKnownUrl));

            RuleFor(x => x.AuthorizationUrl)
                .Must(BeAValidUrl)
                .When(x => x.AuthorizationUrl != null);

            RuleFor(x => x.TokenUrl)
                .Must(BeAValidUrl)
                .When(x => x.TokenUrl != null);

            RuleFor(x => x.UserInfoUrl)
                .Must(BeAValidUrl)
                .When(x => x.UserInfoUrl != null);

            RuleFor(x => x.JwksUri)
                .Must(BeAValidUrl)
                .When(x => x.JwksUri != null);

            RuleFor(x => x.RedirectUris)
                .Must(AllValidUris)
                .When(x => x.RedirectUris != null && x.RedirectUris.Count > 0);

            RuleFor(x => x.GrantTypes)
                .Must(AllNonEmpty)
                .When(x => x.GrantTypes != null && x.GrantTypes.Count > 0);

            RuleFor(x => x.InitialRoles)
                .Must(AllNonEmpty)
                .When(x => x.InitialRoles != null && x.InitialRoles.Count > 0);

            RuleFor(x => x.InitialPermissions)
                .Must(AllNonEmpty)
                .When(x => x.InitialPermissions != null && x.InitialPermissions.Count > 0);
        }

        private static bool BeInProviderTypes(string? value)
        {
            return value == "social" || value == "blocks-oidc" || value == "byos";
        }

        private static bool BeInProtocols(string? value)
        {
            return value == "oidc" || value == "oauth2" || value == "saml" || value == "ldap";
        }

        private static bool BeAValidUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
                (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private static bool AllValidUris(List<string> values)
        {
            return values.All(v => !string.IsNullOrWhiteSpace(v) &&
                Uri.TryCreate(v, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
        }

        private static bool AllNonEmpty(List<string> values)
        {
            return values.All(v => !string.IsNullOrWhiteSpace(v));
        }
    }
}
