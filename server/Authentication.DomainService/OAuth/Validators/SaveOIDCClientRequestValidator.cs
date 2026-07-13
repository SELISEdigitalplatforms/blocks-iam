using Authentication.DomainService.RequestModel;
using FluentValidation;

namespace Authentication.DomainService.OAuth
{
    public sealed class SaveOIDCClientRequestValidator : AbstractValidator<SaveOIDCClientRequest>
    {
        public SaveOIDCClientRequestValidator()
        {
            RuleFor(x => x.ItemId)
                .MaximumLength(128)
                .Must(BeUrlSafe)
                .When(x => !string.IsNullOrWhiteSpace(x.ItemId));

            RuleFor(x => x.ClientType)
                .Must(BeOneOfPublicOrConfidential)
                .When(x => !string.IsNullOrWhiteSpace(x.ClientType));

            RuleFor(x => x.RedirectUris)
                .Must(AllValidUris)
                .When(x => x.RedirectUris != null && x.RedirectUris.Count > 0);

            RuleFor(x => x.AllowedResponseTypes)
                .Must(ContainCode)
                .When(x => x.AllowedResponseTypes != null && x.AllowedResponseTypes.Count > 0);

            RuleFor(x => x.ExternalDiscoveryEndpoint)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.ExternalDiscoveryEndpoint));

            RuleFor(x => x.ClientLogoUrl)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.ClientLogoUrl));

            RuleFor(x => x.FrontChannelLogoutUri)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.FrontChannelLogoutUri));

            RuleFor(x => x.BackChannelLogoutUri)
                .Must(BeValidHttpsUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.BackChannelLogoutUri));

            RuleFor(x => x.PostLogoutRedirectUris)
                .Must(AllValidUris)
                .When(x => x.PostLogoutRedirectUris != null && x.PostLogoutRedirectUris.Count > 0);

            RuleFor(x => x.AllowedScopes)
                .Must(AllNonEmpty)
                .When(x => x.AllowedScopes != null && x.AllowedScopes.Count > 0);
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
            return values.All(v => !string.IsNullOrWhiteSpace(v) &&
                Uri.TryCreate(v, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
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
