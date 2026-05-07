using Blocks.Genesis;
using Authentication.DomainService.Dtos;
using Authentication.DomainService.OAuth.ResponseModel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Services;
using Authentication.DomainService.Utilities;
using System.Security.Claims;
using System.Text.Json.Serialization;
namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Token Lifecycle Management Service
    /// Thin BFF proxy: delegates to OIDC, manages cookies only
    /// External to core authentication - stateless token operations
    /// </summary>
    public class TokenLifecycleService : ITokenLifecycleService
    {
        private readonly IHttpService _httpService;
        private readonly ITenants _tenants;
        private readonly ILogger<TokenLifecycleService> _logger;
        private readonly IAuthenticationService _authenticationService;
        private readonly IAuthenticationRepository _authenticationRepository;

        public TokenLifecycleService(
            IHttpService httpService,
            ITenants tenants,
            ILogger<TokenLifecycleService> logger,
            IAuthenticationService authenticationService,
            IAuthenticationRepository authenticationRepository)
        {
            _httpService = httpService;
            _tenants = tenants;
            _logger = logger;
            _authenticationService = authenticationService;
            _authenticationRepository = authenticationRepository;
        }

        /// <summary>
        /// Exchange authorization code for tokens (OIDC Authorization Code Flow + PKCE)
        /// Tokens are written to secure HttpOnly cookies; response body includes metadata only
        /// </summary>
        public async Task<IActionResult> ExchangeOidcCodeAsync(OidcCodeExchangeRequest request, HttpRequest httpRequest, HttpResponse httpResponse)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "code is required" });

            if (string.IsNullOrWhiteSpace(request.CodeVerifier))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "code_verifier is required" });

            if (string.IsNullOrWhiteSpace(request.ClientId))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "client_id is required" });

            if (string.IsNullOrWhiteSpace(request.RedirectUri))
                return new BadRequestObjectResult(new { error = "invalid_request", error_description = "redirect_uri is required" });

            var tokenEndpoint = httpRequest.Scheme + "://" + httpRequest.Host + "/api/oidc/token";
            var form = new Dictionary<string, string>
            {
                { "grant_type", "authorization_code" },
                { "code", request.Code },
                { "code_verifier", request.CodeVerifier },
                { "client_id", request.ClientId },
                { "redirect_uri", request.RedirectUri }
            };

            if (!string.IsNullOrWhiteSpace(request.TenantId))
            {
                form["tenant_id"] = request.TenantId;
            }

            var (tokenResponse, error) = await _httpService.SendFormUrlEncoded<OidcTokenEndpointResponse>(HttpMethod.Post, form, tokenEndpoint);
            if (!string.IsNullOrWhiteSpace(error) || tokenResponse == null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                _logger.LogWarning("OIDC exchange failed: {Error}", error ?? "empty token response");
                return new BadRequestObjectResult(new { error = "invalid_grant", error_description = "Code exchange failed" });
            }

            var tenantId = !string.IsNullOrWhiteSpace(request.TenantId)
                ? request.TenantId!
                : httpRequest.HttpContext.User.FindFirst("tenant_id")?.Value ?? "default";

            // Get OIDC client registration to check cookie configuration
            var clientRegistration = await _authenticationRepository.GetOidcClientRegistrationAsync(request.ClientId);
            if (clientRegistration == null)
            {
                _logger.LogWarning("OIDC client registration not found for clientId: {ClientId}", request.ClientId);
                return new BadRequestObjectResult(new { error = "invalid_client", error_description = "Client not configured" });
            }

            // Check if client wants tokens in cookies or response
            var useTokensCookie = clientRegistration.UseTokensCookie; // Default is true from entity

            // Build token response object for conditional handling
            var cookieDomain = _tenants.GetTenantByID(tenantId)?.CookieDomain;
            var tokenResponseObj = new TokenResponse
            {
                AccessToken = tokenResponse.AccessToken,
                RefreshToken = tokenResponse.RefreshToken,
                IdToken = tokenResponse.IdToken,
                TokenType = tokenResponse.TokenType ?? "Bearer",
                ExpiresUtc = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn ?? 3600),
                RefreshExpiresUtc = DateTime.UtcNow.AddDays(30),
                Scope = tokenResponse.Scope,
                CookieDomain = cookieDomain
            };

            // Handle tokens conditionally based on client configuration
            var responseBody = await _authenticationService.HandleTokenResponseConditionallyAsync(
                tokenResponseObj,
                httpResponse,
                useTokensCookie,
                request.ClientId);

            return new OkObjectResult(responseBody);
        }

        private sealed class OidcTokenEndpointResponse
        {
            [JsonPropertyName("access_token")]
            public string? AccessToken { get; set; }

            [JsonPropertyName("refresh_token")]
            public string? RefreshToken { get; set; }

            [JsonPropertyName("id_token")]
            public string? IdToken { get; set; }

            [JsonPropertyName("token_type")]
            public string? TokenType { get; set; }

            [JsonPropertyName("expires_in")]
            public int? ExpiresIn { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }
        }

    }
}
