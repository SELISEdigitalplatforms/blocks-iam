using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Authentication.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Blocks.Api.Controllers
{
    [ApiController]
    [Route("oidc")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IAuthorizationFlowService _authorizationFlowService;
        private readonly IOidcCallbackHandler _oidcCallbackHandler;
        private readonly IAuthenticationService _authenticationService;
        private readonly DeviceAuthorizationEndpoint _deviceAuthorizationEndpoint;


        public AuthorizationController(IAuthorizationFlowService authorizationFlowService,
                                       IOidcCallbackHandler oidcCallbackHandler,
                                       IAuthenticationService authenticationService,
                                       DeviceAuthorizationEndpoint deviceAuthorizationEndpoint)
        {
            _authorizationFlowService = authorizationFlowService;
            _oidcCallbackHandler = oidcCallbackHandler;
            _authenticationService = authenticationService;
            _deviceAuthorizationEndpoint = deviceAuthorizationEndpoint;
        }

        /// <summary>
        /// OIDC Login Endpoint — authenticates credentials, sets IDP session, then issues authorization code.
        /// Use this for headless / API-based OIDC flows where the browser UI is not available.
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> OidcLogin([FromBody] OidcLoginRequest request)
        {
            return await _authorizationFlowService.ExecuteOidcLoginAsync(request, Request, Response);
        }

        /// <summary>
        /// OAuth 2.0 Authorization Endpoint (RFC 6749 Section 3.1)
        /// Initiates authorization code flow with PKCE
        /// </summary>
        [HttpGet("authorize")]
        [AllowAnonymous]
        public async Task<IActionResult> Authorize(
            [FromQuery] string client_id,
            [FromQuery] string response_type,
            [FromQuery] string redirect_uri,
            [FromQuery] string scope,
            [FromQuery] string state,
            [FromQuery] string? nonce,
            [FromQuery] string? code_challenge,
            [FromQuery] string code_challenge_method = "S256",
            [FromQuery] string? prompt = null,
            [FromQuery] string? tenant_id = null)
        {
            
            return await _authorizationFlowService.AuthorizeAsync(
                client_id,
                response_type,
                redirect_uri,
                scope,
                state,
                nonce,
                code_challenge ?? string.Empty,
                code_challenge_method,
                prompt,
                tenant_id,
                Request,
                Response);
        }

        /// <summary>
        /// OAuth 2.0 Token Endpoint (RFC 6749 Section 3.2)
        /// Supports both authorization_code and refresh_token grants
        /// </summary>
        [HttpPost("token")]
        [AllowAnonymous]
        public async Task<IActionResult> Token([FromForm] string grant_type)
        {
            return await _authorizationFlowService.TokenAsync(grant_type, Request);
        }

        /// <summary>
        /// OAuth 2.0 Device Authorization Endpoint (RFC 8628 Section 3.1).
        /// Accepts application/x-www-form-urlencoded <c>client_id</c>, <c>scope</c>
        /// and returns device_code, user_code, verification_uri, expires_in, interval.
        /// </summary>
        [HttpPost("device_authorization")]
        [AllowAnonymous]
        public async Task<IActionResult> DeviceAuthorization(CancellationToken ct)
        {
            return await _deviceAuthorizationEndpoint.HandleAsync(Request, ct);
        }

        #region OIDC Federated Authentication (OpenID Connect 1.0)

        /// <summary>
        /// OIDC callback handler (Browser Redirect Pattern - GET)
        /// Receives authorization code from provider via browser redirect
        /// Exchanges code for tokens, validates JWT signature and claims
        /// RFC 6749: OAuth 2.0 | RFC 3986: OpenID Connect | RFC 7519: JWT | RFC 5280: X.509
        /// </summary>
        [HttpGet("callback")]
        [HttpPost("callback")]
        [AllowAnonymous]
        public async Task<IActionResult> HandleOidcCallbackGet(
            [FromQuery] string code,
            [FromQuery] string state,
            [FromBody] OidcCallbackRequest? request = null)
        {
            if (request != null)
            {
                code = request.Code;
                state = request.State;
            }

            if (string.IsNullOrWhiteSpace(code))
                return BadRequest(new { error = "authorization_code_missing", error_description = "Authorization code is required" });

            if (string.IsNullOrWhiteSpace(state))
                return BadRequest(new { error = "state_missing", error_description = "State parameter is required" });

            return await ProcessOidcCallback(code, state);
        }

        private async Task<IActionResult> ProcessOidcCallback(string code, string state)
        {
            // Resolve the provider callback back into the original OIDC request context.
            var result = await _oidcCallbackHandler.HandleCallbackAsync(code, state);

            if (!result.IsSuccess)
            {
                return BuildCallbackFailure(result);
            }

            return await _authorizationFlowService.AuthorizeAsync(
                result.ClientId,
                "code",
                result.RedirectUri,
                result.Scope ?? "openid profile email offline_access",
                result.OriginalState ?? string.Empty,
                result.Nonce ?? string.Empty,
                result.CodeChallenge ?? string.Empty,
                result.CodeChallengeMethod ?? "S256",
                null,
                result.TenantId ?? string.Empty,
                Request,
                Response,
                result.BlocksUserId,
                true);
        }

        /// <summary>
        /// A failed provider callback arrives as a browser navigation, so a JSON body leaves the
        /// user looking at raw error text in the address bar. When the OIDC request context
        /// survived the failure we send them back to the login page they started from, carrying
        /// <c>error</c> and <c>error_description</c> — the same way <c>/oidc/authorize</c> hands
        /// its errors to a login screen. Callers that POST here are APIs, not browsers, and keep
        /// getting the JSON body.
        /// </summary>
        private IActionResult BuildCallbackFailure(OidcCallbackResult result)
        {
            var canReturnToLogin = HttpMethods.IsGet(Request.Method)
                && !string.IsNullOrWhiteSpace(result.ClientId)
                && !string.IsNullOrWhiteSpace(result.RedirectUri);

            if (canReturnToLogin)
            {
                var loginUrl = OidcRedirectUrlBuilder.BuildLoginUrl(
                    result.ClientId!,
                    "code",
                    result.RedirectUri!,
                    result.Scope ?? "openid profile email offline_access",
                    result.OriginalState ?? string.Empty,
                    result.Nonce ?? string.Empty,
                    result.CodeChallenge ?? string.Empty,
                    result.CodeChallengeMethod ?? "S256",
                    result.TenantId);

                var errorParams = new Dictionary<string, string>
                {
                    { "error", result.ErrorCode ?? "access_denied" },
                    { "error_description", result.ErrorMessage ?? "Sign-in could not be completed." }
                };

                return Redirect(OidcRedirectUrlBuilder.BuildRedirectUri(loginUrl, errorParams));
            }

            return BadRequest(new
            {
                error = result.ErrorCode ?? "token_exchange_failed",
                error_description = result.ErrorMessage
            });
        }

        #endregion

        public class SelectAccountSelectionRequest
        {
            [JsonPropertyName("user_id")]
            public string? UserId { get; set; }

            [JsonPropertyName("tenant_id")]
            public string? TenantId { get; set; }
        }

        public class OidcLoginSelectAccountRequest
        {
            [JsonPropertyName("user_id")]
            public string? UserId { get; set; }

            [JsonPropertyName("tenant_id")]
            public string? TenantId { get; set; }

            [JsonPropertyName("client_id")]
            public string? ClientId { get; set; }

            [JsonPropertyName("redirect_uri")]
            public string? RedirectUri { get; set; }

            [JsonPropertyName("scope")]
            public string? Scope { get; set; }

            [JsonPropertyName("state")]
            public string? State { get; set; }

            [JsonPropertyName("nonce")]
            public string? Nonce { get; set; }

            [JsonPropertyName("code_challenge")]
            public string? CodeChallenge { get; set; }

            [JsonPropertyName("code_challenge_method")]
            public string? CodeChallengeMethod { get; set; }
        }
    }
}
