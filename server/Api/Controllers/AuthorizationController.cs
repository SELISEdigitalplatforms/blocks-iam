using Authentication.DomainService.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Blocks.Api.Controllers
{
    [ApiController]
    [Route("oidc")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IAuthorizationFlowService _authorizationFlowService;

        public AuthorizationController(IAuthorizationFlowService authorizationFlowService)
        {
            _authorizationFlowService = authorizationFlowService;
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
        /// OIDC Login Select Account — continues OIDC login after user selects account from multiple options
        /// </summary>
        [HttpPost("login/select-account")]
        [AllowAnonymous]
        public async Task<IActionResult> OidcLoginSelectAccount([FromBody] OidcLoginSelectAccountRequest? request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.TenantId))
                return BadRequest(new { error = "invalid_request", error_description = "tenant_id is required" });

            return await _authorizationFlowService.ContinueOidcLoginAfterAccountSelectionAsync(
                request.UserId ?? string.Empty,
                request.TenantId,
                request.ClientId ?? string.Empty,
                request.RedirectUri ?? string.Empty,
                request.Scope,
                request.State,
                request.Nonce,
                request.CodeChallenge,
                request.CodeChallengeMethod,
                Request,
                Response);
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
            [FromQuery] string nonce,
            [FromQuery] string code_challenge,
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
                code_challenge,
                code_challenge_method,
                prompt,
                tenant_id,
                User,
                Request,
                Response);
        }

        [HttpPost("account/select")]
        [Authorize]
        public async Task<IActionResult> SelectAccount([FromBody] SelectAccountSelectionRequest? request)
        {
            return await _authorizationFlowService.SelectAccountAsync(request?.UserId ?? string.Empty, request?.TenantId, Request, Response);
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
