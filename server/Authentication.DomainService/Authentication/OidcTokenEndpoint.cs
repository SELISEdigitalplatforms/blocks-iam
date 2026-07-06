using Authentication.DomainService.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Dispatches the OIDC token endpoint to the grant-specific issuer:
    /// <list type="bullet">
    /// <item><c>authorization_code</c> → <see cref="AuthorizationCodeExchangeService"/></item>
    /// <item><c>refresh_token</c>      → <see cref="OidcRefreshTokenService"/></item>
    /// <item><c>client_credentials</c> → <see cref="ClientCredentialsTokenIssuer"/></item>
    /// </list>
    /// Replaces the giant <c>AuthorizationFlowService.TokenAsync</c> while keeping the contract intact.
    /// </summary>
    public sealed class OidcTokenEndpoint
    {
        private readonly AuthorizationCodeExchangeService _exchangeService;
        private readonly OidcRefreshTokenService _refreshService;
        private readonly ClientCredentialsTokenIssuer _clientCredentialsIssuer;
        private readonly ILogger<OidcTokenEndpoint> _logger;

        public OidcTokenEndpoint(
            AuthorizationCodeExchangeService exchangeService,
            OidcRefreshTokenService refreshService,
            ClientCredentialsTokenIssuer clientCredentialsIssuer,
            ILogger<OidcTokenEndpoint> logger)
        {
            _exchangeService = exchangeService;
            _refreshService = refreshService;
            _clientCredentialsIssuer = clientCredentialsIssuer;
            _logger = logger;
        }

        public async Task<IActionResult> TokenAsync(string grantType, HttpRequest request)
        {
            try
            {
                if (grantType == GrantTypes.AuthCode)
                {
                    return await _exchangeService.ExchangeAsync(request);
                }

                if (grantType == GrantTypes.RefreshToken)
                {
                    return await _refreshService.RotateAsync(request);
                }

                if (grantType == GrantTypes.ClientCredential)
                {
                    return await _clientCredentialsIssuer.IssueAsync(request);
                }

                return new BadRequestObjectResult(new { error = "unsupported_grant_type", error_description = $"Grant type '{grantType}' not supported" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token endpoint");
                return new ObjectResult(new { error = "server_error" }) { StatusCode = 500 };
            }
        }
    }
}
