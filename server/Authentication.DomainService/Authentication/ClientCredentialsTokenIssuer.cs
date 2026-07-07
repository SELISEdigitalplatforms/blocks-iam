using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.ResponseModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Authentication.DomainService.Shared;
using Blocks.Genesis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Issues tokens for the OAuth 2.0 <c>client_credentials</c> grant.
    /// Extracted from <c>AuthorizationFlowService</c> to reduce god-service size.
    /// </summary>
    public sealed class ClientCredentialsTokenIssuer
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ClientCredentialAuthorizationService _clientCredentialAuthorizationService;

        public ClientCredentialsTokenIssuer(
            IAuthenticationRepository authenticationRepository,
            ClientCredentialAuthorizationService clientCredentialAuthorizationService)
        {
            _authenticationRepository = authenticationRepository;
            _clientCredentialAuthorizationService = clientCredentialAuthorizationService;
        }

        public async Task<IActionResult> IssueAsync(HttpRequest request)
        {
            var clientId = request.Form["client_id"].ToString();
            var clientSecret = request.Form["client_secret"].ToString();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                TryReadBasicClientAuthentication(request, out clientId, out clientSecret);
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Missing client authentication" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return new BadRequestObjectResult(new { error = "server_error", error_description = "Authentication configuration missing" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.ClientCredential,
                ClientId = clientId,
                ClientSecret = clientSecret,
                Request = request
            };

            var result = await _clientCredentialAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                var statusCode = string.Equals(result.Error, "invalid_client", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status400BadRequest;

                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                { StatusCode = statusCode };
            }

            return new OkObjectResult(new TokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = result.ExpiresIn
            });
        }

        private static void TryReadBasicClientAuthentication(HttpRequest request, out string clientId, out string clientSecret)
        {
            clientId = string.Empty;
            clientSecret = string.Empty;
            if (!request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                return;
            }

            var header = authHeader.ToString();
            if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                var encoded = header.Substring("Basic ".Length).Trim();
                var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                var idx = decoded.IndexOf(':');
                if (idx > 0)
                {
                    clientId = decoded.Substring(0, idx);
                    clientSecret = decoded.Substring(idx + 1);
                }
            }
            catch
            {
                // Malformed basic auth header - leave clientId/clientSecret empty.
            }
        }
    }
}
