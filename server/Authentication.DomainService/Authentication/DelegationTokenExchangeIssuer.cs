using Authentication.DomainService.OAuth;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.OAuth.Services;
using Authentication.DomainService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Authentication.DomainService.Authentication
{
    /// <summary>
    /// Reads the RFC 8693 form off the token endpoint and shapes the OAuth response.
    /// <para>
    /// This lives on the existing OIDC token endpoint -- no alias, no new route. Being under
    /// <c>/oidc</c> does not make it OIDC-only: it is a plain form POST and any service may call
    /// it. The effective path carries whatever <c>ApiRouting:Prefix</c> is configured, which is why
    /// Genesis resolves it by discovery instead of hardcoding it.
    /// </para>
    /// </summary>
    public sealed class DelegationTokenExchangeIssuer
    {
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly TokenExchangeAuthorizationService _tokenExchangeAuthorizationService;

        public DelegationTokenExchangeIssuer(
            IAuthenticationRepository authenticationRepository,
            TokenExchangeAuthorizationService tokenExchangeAuthorizationService)
        {
            _authenticationRepository = authenticationRepository;
            _tokenExchangeAuthorizationService = tokenExchangeAuthorizationService;
        }

        public async Task<IActionResult> ExchangeAsync(HttpRequest request)
        {
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return new ObjectResult(new { error = "server_error", error_description = "Authentication configuration missing" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError
                };
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.TokenExchange,
                Request = request,
                TokenExchange = new TokenExchangeRequest
                {
                    SubjectToken = request.Form["subject_token"].ToString(),
                    SubjectTokenType = request.Form["subject_token_type"].ToString(),
                    Nonce = request.Form["nonce"].ToString(),
                    Ts = request.Form["ts"].ToString(),
                    Signature = request.Form["sig"].ToString()
                }
            };

            var result = await _tokenExchangeAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                return new ObjectResult(new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                })
                {
                    StatusCode = result.StatusCode == 0 ? StatusCodes.Status400BadRequest : result.StatusCode
                };
            }

            // snake_case, and no refresh_token: a delegated token is renewed by redeeming the
            // grant again, never by rotation.
            return new OkObjectResult(new
            {
                access_token = result.AccessToken,
                token_type = "Bearer",
                expires_in = result.ExpiresIn
            });
        }
    }
}
