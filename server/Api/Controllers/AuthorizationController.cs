using Microsoft.AspNetCore.Mvc;
using Authentication.DomainService.Authentication;
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
        /// OAuth 2.0 Authorization Endpoint (RFC 6749 Section 3.1)
        /// Initiates authorization code flow with PKCE
        /// </summary>
        [HttpGet("authorize")]
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
        public async Task<IActionResult> SelectAccount([FromBody] SelectAccountSelectionRequest? request)
        {
            return await _authorizationFlowService.SelectAccountAsync(request?.UserId ?? string.Empty, request?.TenantId, Request, Response);
        }

        /// <summary>
        /// OAuth 2.0 Token Endpoint (RFC 6749 Section 3.2)
        /// Supports both authorization_code and refresh_token grants
        /// </summary>
        [HttpPost("token")]
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
    }
}
