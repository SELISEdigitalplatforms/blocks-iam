using Authentication.DomainService.Shared;
using FluentValidation;
using System.Net.Http.Json;
using System.Text.Json;

namespace Authentication.DomainService.OAuth
{
    public sealed class SaveSsoCredentialRequestValidator : AbstractValidator<SaveSsoCredentialRequest>
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public SaveSsoCredentialRequestValidator(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;

            RuleFor(x => x.WellKnownUrl)
                .Cascade(CascadeMode.Stop)
                .Must(BeAValidUrl)
                    .WithMessage("WellKnownUrl must be a valid URL")
                .MustAsync(HaveValidOidcMetadataAsync)
                    .WithMessage("WellKnownUrl does not expose valid OpenID Connect metadata")
                .When(x => x.SSOType == SSOType.BYOSSO);
        }

        private static bool BeAValidUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult) &&
                   (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        private async Task<bool> HaveValidOidcMetadataAsync(string wellKnownUrl, CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = _httpClientFactory.CreateClient(nameof(SaveSsoCredentialRequestValidator));

                var response = await httpClient.GetAsync(wellKnownUrl, cancellationToken);
                if (!response.IsSuccessStatusCode) return false;

                // Try parsing metadata
                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
                if (jsonDoc == null) return false;

                // Basic validation: must contain "authorization_endpoint" and "token_endpoint"
                return jsonDoc.RootElement.TryGetProperty("authorization_endpoint", out _) &&
                       jsonDoc.RootElement.TryGetProperty("token_endpoint", out _);
            }
            catch
            {
                return false;
            }
        }
    }
}