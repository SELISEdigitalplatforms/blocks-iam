using Authentication.DomainService.Shared.RequestModel;
using FluentValidation;
using System.Net.Http.Json;
using System.Text.Json;

namespace Authentication.DomainService.OAuth
{
    public sealed class SaveIdentityProviderRequestValidator : AbstractValidator<SaveIdentityProviderRequest>
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public SaveIdentityProviderRequestValidator(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;

            RuleFor(x => x.Provider)
                .NotEmpty().WithMessage("Provider is required.");

            RuleFor(x => x.ProviderType)
                .NotEmpty().WithMessage("ProviderType is required.")
                .Must(BeInProviderTypes).WithMessage("ProviderType must be one of: social, byos orblocks-oidc");

            RuleFor(x => x.Protocol)
                .NotEmpty().WithMessage("Protocol is required.")
                .Must(BeInProtocols).WithMessage("Protocol must be one of: oidc, oauth2, saml, ldap.");

            RuleFor(x => x.ClientId)
                .NotEmpty().WithMessage("ClientId is required.");

            RuleFor(x => x.WellKnownUrl)
                .Cascade(CascadeMode.Stop)
                .Must(BeAValidUrl)
                    .WithMessage("WellKnownUrl must be a valid http(s) URL.")
                .MustAsync(HaveValidOidcMetadataAsync)
                    .WithMessage("WellKnownUrl does not expose valid OpenID Connect metadata.")
                .When(x => !string.IsNullOrWhiteSpace(x.WellKnownUrl));

            RuleFor(x => x.AuthorizationUrl)
                .Must(BeAValidUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.AuthorizationUrl));

            RuleFor(x => x.TokenUrl)
                .Must(BeAValidUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.TokenUrl));

            RuleFor(x => x.UserInfoUrl)
                .Must(BeAValidUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.UserInfoUrl));

            RuleFor(x => x.JwksUri)
                .Must(BeAValidUrl)
                .When(x => !string.IsNullOrWhiteSpace(x.JwksUri));

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
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value is "social" or "byos" or "blocks-oidc";
        }

        private static bool BeInProtocols(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return value is "oidc" or "oauth2" or "saml" or "ldap";
        }

        private static bool BeAValidUrl(string? url)
        {
            return !string.IsNullOrWhiteSpace(url) &&
                Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
                (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private async Task<bool> HaveValidOidcMetadataAsync(string wellKnownUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient(nameof(SaveIdentityProviderRequestValidator));

                var response = await httpClient.GetAsync(wellKnownUrl, cancellationToken);
                if (!response.IsSuccessStatusCode) return false;

                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
                if (jsonDoc == null) return false;

                return jsonDoc.RootElement.TryGetProperty("authorization_endpoint", out _) &&
                       jsonDoc.RootElement.TryGetProperty("token_endpoint", out _);
            }
            catch
            {
                return false;
            }
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
