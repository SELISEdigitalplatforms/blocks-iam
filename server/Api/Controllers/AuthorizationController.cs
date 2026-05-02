using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DomainService.Oidc.Repositories;
using DomainService.Oidc.Validation;
using Blocks.Genesis.Auth;
using Blocks.Genesis.Auth.Services;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;

namespace Blocks.Api.Controllers
{
    [ApiController]
    [Route("api/oidc")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IAuthorizationCodeRepository _authCodeRepo;
        private readonly IOAuthClientRepository _clientRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly IIdpSessionRepository _sessionRepo;
        private readonly IAuditLogRepository _auditLogRepo;
        private readonly ITokenGenerationService _tokenService;
        private readonly IPkceService _pkceService;
        private readonly AuthorizeRequestValidator _authorizeValidator;
        private readonly ILogger<AuthorizationController> _logger;

        public AuthorizationController(
            IAuthorizationCodeRepository authCodeRepo,
            IOAuthClientRepository clientRepo,
            IRefreshTokenRepository refreshTokenRepo,
            IIdpSessionRepository sessionRepo,
            IAuditLogRepository auditLogRepo,
            ITokenGenerationService tokenService,
            IPkceService pkceService,
            AuthorizeRequestValidator authorizeValidator,
            ILogger<AuthorizationController> logger)
        {
            _authCodeRepo = authCodeRepo;
            _clientRepo = clientRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _sessionRepo = sessionRepo;
            _auditLogRepo = auditLogRepo;
            _tokenService = tokenService;
            _pkceService = pkceService;
            _authorizeValidator = authorizeValidator;
            _logger = logger;
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
            [FromQuery] string prompt = null)
        {
            try
            {
                // Validate request parameters
                var authorizeRequest = new AuthorizeRequest
                {
                    ClientId = client_id,
                    ResponseType = response_type,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    State = state,
                    Nonce = nonce,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    Prompt = prompt
                };

                var validationResult = _authorizeValidator.Validate(authorizeRequest);
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Authorization request validation failed for {client_id}: {string.Join(", ", validationResult.Errors)}");
                    
                    var errorParams = new Dictionary<string, string>
                    {
                        { "error", "invalid_request" },
                        { "error_description", string.Join("; ", validationResult.Errors) },
                        { "state", state }
                    };
                    return Redirect(BuildRedirectUri(redirect_uri, errorParams));
                }

                // Check if user is authenticated
                var userId = User.FindFirst("sub")?.Value;
                if (string.IsNullOrEmpty(userId))
                {
                    _logger.LogInformation($"Unauthenticated authorization request for {client_id}");
                    return Unauthorized(new { error = "unauthorized", error_description = "User not authenticated" });
                }

                var tenantId = User.FindFirst("tenant_id")?.Value;

                // Validate client
                var client = await _clientRepo.GetByClientIdAsync(client_id, tenantId);
                if (client == null)
                {
                    _logger.LogWarning($"Unknown client: {client_id}");
                    return BadRequest(new { error = "invalid_client" });
                }

                // Validate redirect_uri
                if (!client.RedirectUris.Contains(redirect_uri))
                {
                    _logger.LogWarning($"Invalid redirect_uri for {client_id}: {redirect_uri}");
                    return BadRequest(new { error = "invalid_request", error_description = "Invalid redirect_uri" });
                }

                // Generate authorization code (one-time use, 10 minute TTL)
                var authCode = GenerateRandomCode(32);
                var codeModel = new AuthorizationCodeModel
                {
                    Code = authCode,
                    ClientId = client_id,
                    TenantId = tenantId,
                    UserId = userId,
                    RedirectUri = redirect_uri,
                    Scope = scope,
                    Nonce = nonce,
                    State = state,
                    CodeChallenge = code_challenge,
                    CodeChallengeMethod = code_challenge_method,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(10),
                    CreatedAt = DateTime.UtcNow,
                    CreatedByIpAddress = GetClientIpAddress(),
                    IsUsed = false
                };

                await _authCodeRepo.CreateAsync(codeModel);

                _logger.LogInformation($"Authorization code issued for user {userId}, client {client_id}");

                // Redirect to callback with code and state
                var callbackParams = new Dictionary<string, string>
                {
                    { "code", authCode },
                    { "state", state }
                };

                return Redirect(BuildRedirectUri(redirect_uri, callbackParams));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in authorization endpoint");
                return StatusCode(500, new { error = "server_error", error_description = "Internal server error" });
            }
        }

        /// <summary>
        /// OAuth 2.0 Token Endpoint (RFC 6749 Section 3.2)
        /// Supports both authorization_code and refresh_token grants
        /// </summary>
        [HttpPost("token")]
        [AllowAnonymous]
        public async Task<IActionResult> Token([FromForm] string grant_type)
        {
            try
            {
                if (grant_type == "authorization_code")
                {
                    return await ExchangeAuthorizationCode();
                }
                else if (grant_type == "refresh_token")
                {
                    return await RotateRefreshToken();
                }
                else
                {
                    return BadRequest(new { error = "unsupported_grant_type", error_description = $"Grant type '{grant_type}' not supported" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in token endpoint");
                return StatusCode(500, new { error = "server_error" });
            }
        }

        /// <summary>
        /// Exchange authorization code for tokens (RFC 6749 Section 4.1.3)
        /// </summary>
        private async Task<IActionResult> ExchangeAuthorizationCode()
        {
            var code = Request.Form["code"].ToString();
            var code_verifier = Request.Form["code_verifier"].ToString();
            var client_id = Request.Form["client_id"].ToString();
            var redirect_uri = Request.Form["redirect_uri"].ToString();

            // Validate required parameters
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(code_verifier) || string.IsNullOrEmpty(client_id))
            {
                return BadRequest(new { error = "invalid_request", error_description = "Missing required parameters" });
            }

            var tenantId = User.FindFirst("tenant_id")?.Value;

            // Fetch authorization code from DB
            var authCode = await _authCodeRepo.GetByCodeAsync(code);
            if (authCode == null)
            {
                _logger.LogWarning($"Authorization code not found: {code}");
                return BadRequest(new { error = "invalid_grant", error_description = "Authorization code is invalid or expired" });
            }

            // Check expiry
            if (authCode.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning($"Authorization code expired: {code}");
                return BadRequest(new { error = "invalid_grant", error_description = "Authorization code has expired" });
            }

            // ONE-TIME USE ENFORCEMENT: Check if already used
            if (authCode.IsUsed)
            {
                _logger.LogCritical($"REUSE ATTACK DETECTED: Code reused by IP {GetClientIpAddress()}, original IP {authCode.UsedByIpAddress}. Revoking token family.");
                await RevokeUserTokens(authCode.UserId, authCode.ClientId, authCode.TenantId ?? tenantId);
                return BadRequest(new { error = "invalid_grant", error_description = "Authorization code has already been used" });
            }

            // Validate client
            var client = await _clientRepo.GetByClientIdAsync(client_id, tenantId ?? authCode.TenantId);
            if (client == null || client.ClientId != authCode.ClientId)
            {
                _logger.LogWarning($"Client validation failed for code exchange");
                return BadRequest(new { error = "invalid_client" });
            }

            // Validate redirect_uri
            if (authCode.RedirectUri != redirect_uri)
            {
                _logger.LogWarning($"Redirect URI mismatch for code exchange");
                return BadRequest(new { error = "invalid_grant", error_description = "Redirect URI mismatch" });
            }

            // PKCE VALIDATION: Validate code_verifier against code_challenge
            var pkceValid = await _pkceService.ValidateVerifierAsync(authCode.CodeChallenge, code_verifier, authCode.CodeChallengeMethod);
            if (!pkceValid)
            {
                _logger.LogWarning($"PKCE validation failed for client {client_id}");
                return BadRequest(new { error = "invalid_grant", error_description = "PKCE code_verifier is invalid" });
            }

            // Mark code as USED
            var markUsedSuccess = await _authCodeRepo.MarkAsUsedAsync(code, DateTime.UtcNow, GetClientIpAddress());
            if (!markUsedSuccess)
            {
                _logger.LogWarning($"Failed to mark authorization code as used: {code}");
                return BadRequest(new { error = "invalid_grant", error_description = "Could not process authorization code" });
            }

            // Generate tokens
            var claims = new OidcClaims
            {
                Sub = authCode.UserId,
                TenantId = authCode.TenantId ?? tenantId,
                Nonce = authCode.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id
            };

            var issuer = $"https://{Request.Host}/";
            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, 3600);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, 3600);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer);

            // Store refresh token in DB
            refreshTokenModel.UserId = authCode.UserId;
            refreshTokenModel.ClientId = client_id;
            refreshTokenModel.TenantId = authCode.TenantId ?? tenantId;
            refreshTokenModel.IpAddress = GetClientIpAddress();
            refreshTokenModel.UserAgent = Request.Headers["User-Agent"].ToString();
            await _refreshTokenRepo.CreateAsync(refreshTokenModel);

            _logger.LogInformation($"Tokens issued for user {authCode.UserId}, client {client_id}, family {refreshTokenModel.FamilyId}");

            return Ok(new TokenResponse
            {
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshTokenModel.TokenId,
                    TokenType = "Bearer",
                    ExpiresIn = 3600,
                    Scope = authCode.Scope
                });
        }

        /// <summary>
        /// Rotate refresh token (RFC 6749 Section 6)
        /// Issues new tokens using refresh_token grant
        /// Implements token family tracking for reuse detection
        /// </summary>
        private async Task<IActionResult> RotateRefreshToken()
        {
            var refresh_token = Request.Form["refresh_token"].ToString();
            var client_id = Request.Form["client_id"].ToString();

            if (string.IsNullOrEmpty(refresh_token) || string.IsNullOrEmpty(client_id))
            {
                return BadRequest(new { error = "invalid_request", error_description = "Missing refresh_token or client_id" });
            }

            var tenantId = User.FindFirst("tenant_id")?.Value;

            // Fetch refresh token from DB
            var storedToken = await _refreshTokenRepo.GetByTokenIdAsync(refresh_token);
            if (storedToken == null)
            {
                _logger.LogWarning($"Refresh token not found: {refresh_token}");
                return BadRequest(new { error = "invalid_grant", error_description = "Invalid refresh token" });
            }

            // Check if token is revoked
            if (storedToken.IsRevoked)
            {
                _logger.LogCritical($"REUSE ATTACK DETECTED: Revoked token used again. Original revocation reason: {storedToken.RevokeReason}. Revoking family {storedToken.FamilyId}.");
                // Immediately revoke entire family
                await _refreshTokenRepo.RevokeByFamilyIdAsync(storedToken.FamilyId, "reuse_detected");
                await LogAuditEvent("token_reuse_detected", storedToken.UserId, client_id, tenantId, "CRITICAL");
                return BadRequest(new { error = "invalid_grant", error_description = "Refresh token has been revoked" });
            }

            // Check if token is expired (sliding or absolute)
            if (storedToken.IsExpired())
            {
                _logger.LogWarning($"Refresh token expired: {refresh_token}");
                return BadRequest(new { error = "invalid_grant", error_description = "Refresh token has expired" });
            }

            // Validate client
            var client = await _clientRepo.GetByClientIdAsync(client_id, storedToken.TenantId);
            if (client == null)
            {
                _logger.LogWarning($"Client validation failed for token rotation: {client_id}");
                return BadRequest(new { error = "invalid_client" });
            }

            // Generate new tokens with family tracking
            var claims = new OidcClaims
            {
                Sub = storedToken.UserId,
                TenantId = storedToken.TenantId,
                OrgId = storedToken.OrgId,
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id
            };

            var issuer = $"https://{Request.Host}/";
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, 3600);
            var newRefreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer);

            // Link to token family for reuse detection
            newRefreshTokenModel.FamilyId = storedToken.FamilyId;
            newRefreshTokenModel.ParentTokenId = storedToken.TokenId;
            newRefreshTokenModel.UserId = storedToken.UserId;
            newRefreshTokenModel.ClientId = client_id;
            newRefreshTokenModel.TenantId = storedToken.TenantId;
            newRefreshTokenModel.OrgId = storedToken.OrgId;
            newRefreshTokenModel.SessionId = storedToken.SessionId;
            newRefreshTokenModel.IpAddress = GetClientIpAddress();
            newRefreshTokenModel.UserAgent = Request.Headers["User-Agent"].ToString();

            // Store new token
            await _refreshTokenRepo.CreateAsync(newRefreshTokenModel);

            // Update parent token with child reference
            storedToken.ChildTokenIds.Add(newRefreshTokenModel.TokenId);
            await _refreshTokenRepo.UpdateSlidingExpiryAsync(storedToken.TokenId);

            _logger.LogInformation($"Token rotated for user {storedToken.UserId}, client {client_id}, family {storedToken.FamilyId}");
            await LogAuditEvent("token_refreshed", storedToken.UserId, client_id, storedToken.TenantId, "INFO");

            return Ok(new TokenResponse
            {
                AccessToken = accessToken,
                RefreshToken = newRefreshTokenModel.TokenId,
                TokenType = "Bearer",
                ExpiresIn = 3600,
                Scope = "openid profile email"
            });
        }

        private async Task LogAuditEvent(string eventType, string userId, string clientId, string tenantId, string severity)
        {
            try
            {
                var auditLog = new AuditLogModel
                {
                    EventType = eventType,
                    UserId = userId,
                    ClientId = clientId,
                    TenantId = tenantId,
                    IpAddress = GetClientIpAddress(),
                    UserAgent = Request.Headers["User-Agent"].ToString(),
                    Severity = severity,
                    Status = "success",
                    Timestamp = DateTime.UtcNow
                };
                await _auditLogRepo.CreateAsync(auditLog);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging audit event: {eventType}");
            }
        }

        private string GenerateRandomCode(int length)
        {
            byte[] buffer = new byte[length];
            RandomNumberGenerator.Fill(buffer);
            return Convert.ToBase64String(buffer).Replace("/", "_").Replace("+", "-").Substring(0, 43);
        }

        private string BuildRedirectUri(string baseUri, Dictionary<string, string> parameters)
        {
            var sb = new StringBuilder(baseUri);
            sb.Append(baseUri.Contains("?") ? "&" : "?");
            
            foreach (var param in parameters.Where(p => !string.IsNullOrEmpty(p.Value)))
            {
                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append("=");
                sb.Append(Uri.EscapeDataString(param.Value));
                sb.Append("&");
            }

            return sb.ToString().TrimEnd('&');
        }

        private string GetClientIpAddress()
        {
            if (Request.HttpContext.Connection.RemoteIpAddress != null)
            {
                return Request.HttpContext.Connection.RemoteIpAddress.ToString();
            }
            return "unknown";
        }

        private async Task RevokeUserTokens(string userId, string clientId, string tenantId)
        {
            try
            {
                // Get all active tokens for user-client combination
                var userTokens = await _refreshTokenRepo.GetByUserAsync(userId, tenantId);
                var clientTokens = userTokens.Where(t => t.ClientId == clientId && !t.IsRevoked).ToList();

                // Revoke all token families
                var familyIds = clientTokens.Select(t => t.FamilyId).Distinct();
                foreach (var familyId in familyIds)
                {
                    await _refreshTokenRepo.RevokeByFamilyIdAsync(familyId, "authorization_code_reuse_detected");
                }

                // Log audit event
                await LogAuditEvent("code_reuse_attack", userId, clientId, tenantId, "CRITICAL");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking user tokens for {userId}");
            }
        }
    }

}

