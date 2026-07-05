using Authentication.DomainService.Authentication;
using Authentication.DomainService.OAuth.RequestModel;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Idp.DomainService.Oidc.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Blocks.Api.Controllers
{
    /// <summary>
    /// OpenID Connect Discovery Endpoints
    /// RFC 8414: OAuth 2.0 Authorization Server Metadata
    /// OIDC Core 1.0: OpenID Connect Discovery 1.0
    /// 
    /// These endpoints provide machine-readable metadata for client discovery
    /// and automatic configuration.
    /// </summary>
    [ApiController]
    public class DiscoveryController : ControllerBase
    {
        private readonly IDiscoveryService _discoveryService;
        private readonly IJwksService _jwksService;
        private readonly IAuthenticationFlowService _authenticationFlowService;
        private readonly IConfiguration _configuration;

        public DiscoveryController(
            IDiscoveryService discoveryService, 
            IJwksService jwksService, 
            IAuthenticationFlowService authenticationFlowService,
            IConfiguration configuration
        )
        {
            _discoveryService = discoveryService;
            _jwksService = jwksService;
            _authenticationFlowService = authenticationFlowService;
            _configuration = configuration;
        }

        /// <summary>
        /// OpenID Connect Discovery Endpoint
        /// RFC 8414 Section 3.1 & OIDC Core 1.0 Section 4.1
        /// 
        /// Provides metadata for OIDC providers
        /// Includes: authorization_endpoint, token_endpoint, jwks_uri, etc.
        /// 
        /// Standard response caching: 1 hour
        /// </summary>
        [HttpGet("/{tenant_id}/.well-known/openid-configuration")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(DiscoveryMetadata), 200)]
        public async Task<IActionResult> OpenIdConfiguration()
        {
            try
            {
                var metadata = await _discoveryService.GetMetadataAsync();

                // Cache for 1 hour
                Response.Headers.CacheControl = "public, max-age=3600";

                return Ok(metadata);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to get OpenID Connect configuration");
                return StatusCode(500, new { error = "server_error", error_description = "Failed to retrieve configuration" });
            }
        }

        /// <summary>
        /// OAuth Authorization Server Metadata Endpoint
        /// RFC 8414 Section 3.2
        /// 
        /// Provides metadata for OAuth Authorization Servers
        /// Compatible with both OAuth and OIDC servers
        /// 
        /// Standard response caching: 1 hour
        /// </summary>
        [HttpGet("/{tenant_id}/.well-known/oauth-authorization-server")]
        [AllowAnonymous]
        [Produces("application/json")]
        [ProducesResponseType(typeof(OAuthAuthorizationServerMetadata), 200)]
        public async Task<IActionResult> OAuthAuthorizationServer()
        {
            try
            {
                var metadata = await _discoveryService.GetAuthorizationServerMetadataAsync();

                // Cache for 1 hour
                Response.Headers.CacheControl = "public, max-age=3600";

                return Ok(metadata);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to get OAuth Authorization Server metadata");
                return StatusCode(500, new { error = "server_error", error_description = "Failed to retrieve metadata" });
            }
        }

        /// <summary>
        /// JSON Web Key Set (JWKS) Endpoint
        /// RFC 7517 & RFC 7518
        /// 
        /// Provides public signing keys in JWK format
        /// Used by resource servers and clients to validate token signatures
        /// 
        /// Response MUST be cached by clients for token validation
        /// 
        /// Standard response caching: 24 hours (keys don't change frequently)
        /// </summary>
        [HttpGet("/{tenant_id}/.well-known/jwks.json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(JwksResponse), 200)]
        public async Task<IActionResult> JwksJson()
        {
            try
            {
                var keys = await _jwksService.GetKeysAsync();

                // Cache for 24 hours (keys rotation is infrequent)
                // Clients MUST cache this per JWT spec
                Response.Headers.CacheControl = "public, max-age=86400";

                return Ok(keys);
            }
            catch (System.Exception ex)
            {
                Log.Error(ex, "Failed to get JWKS");
                return StatusCode(500, new { error = "server_error", error_description = "Failed to retrieve JWKS" });
            }
        }

        [HttpGet("/{tenant_id}/jwks.json")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(JwksResponse), 200)]
        public Task<IActionResult> JwksJsonAlias()
        {
            return JwksJson();
        }

        [HttpPost("/login")]
        [Produces("application/json")]
        [ProducesResponseType(typeof(JwksResponse), 200)]
        public async Task<IActionResult> ExecutePasswordLogin([FromBody] EmbeddedLoginRequest request)
        {
            var section = _configuration.GetSection("FrontendRuntime");
            BlocksContext.SetContext(BlocksContext.Create(
                section["BLOCKS_X_BLOCKS_KEY"],
                Array.Empty<string>(),
                string.Empty,
                false,
                string.Empty,
                string.Empty,
                DateTime.MinValue,
                string.Empty,
                Array.Empty<string>(),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                section["BLOCKS_X_BLOCKS_KEY"],
                string.Empty
            ));

            var result = await _authenticationFlowService.ExecuteEmbeddedLoginAsync(request, Request);
            return new OkObjectResult(new
            {
                access_token = result.TokenResponse.AccessToken,
                refresh_token = result.TokenResponse.RefreshToken,
                token_type = result.TokenResponse.TokenType,
                expires_in = result.TokenResponse.ExpiresIn,
                expires_utc = result.TokenResponse.ExpiresUtc,
                refresh_expires_utc = result.TokenResponse.RefreshExpiresUtc,
                scope = result.TokenResponse.Scope,
                id_token = result.TokenResponse.IdToken
            }); ;
        }
    }
}
