using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using DomainService.Oidc.Repositories;
using DomainService.Oidc.Validation;
using Blocks.Genesis.Auth;
using Blocks.Genesis.Auth.Services;
using DomainService.OAuth.RequestModel;
using DomainService.OAuth.ResponseModel;
using DomainService.OAuth;
using DomainService.OAuth.Services;
using Iam.DomainService.Users;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using DomainService.Services;

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
        private readonly IUserRepository _userRepository;
        private readonly IAuthorizationClaimsResolver _authorizationClaimsResolver;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ClientCredentialAuthorizationService _clientCredentialAuthorizationService;
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
            IUserRepository userRepository,
            IAuthorizationClaimsResolver authorizationClaimsResolver,
            IAuthenticationRepository authenticationRepository,
            ClientCredentialAuthorizationService clientCredentialAuthorizationService,
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
            _userRepository = userRepository;
            _authorizationClaimsResolver = authorizationClaimsResolver;
            _authenticationRepository = authenticationRepository;
            _clientCredentialAuthorizationService = clientCredentialAuthorizationService;
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
            [FromQuery] string prompt = null,
            [FromQuery] string session_id = null,
            [FromQuery] string selected_user_id = null,
            [FromQuery] string selected_tenant_id = null,
            [FromQuery] string tenant_id = null)
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

                var claimUserId = User.FindFirst("sub")?.Value;
                var claimTenantId = User.FindFirst("tenant_id")?.Value;
                var effectiveSessionId = !string.IsNullOrWhiteSpace(session_id)
                    ? session_id
                    : Request.Cookies["idp_session_id"];

                string resolvedUserId = null;
                string resolvedTenantId = null;

                if (!string.IsNullOrWhiteSpace(effectiveSessionId))
                {
                    var session = await _sessionRepo.GetBySessionIdAsync(effectiveSessionId);
                    if (session != null && !session.RevokedAt.HasValue && !session.IsExpired())
                    {
                        var sessionAccounts = session.Accounts.AsEnumerable();
                        if (!string.IsNullOrWhiteSpace(tenant_id))
                        {
                            sessionAccounts = sessionAccounts.Where(a => string.Equals(a.TenantId, tenant_id, StringComparison.OrdinalIgnoreCase));
                        }

                        var filteredAccounts = sessionAccounts.ToList();

                        if (!string.IsNullOrWhiteSpace(selected_user_id))
                        {
                            var selectedAccount = filteredAccounts.FirstOrDefault(a =>
                                string.Equals(a.UserId, selected_user_id, StringComparison.OrdinalIgnoreCase)
                                && (string.IsNullOrWhiteSpace(selected_tenant_id)
                                    || string.Equals(a.TenantId, selected_tenant_id, StringComparison.OrdinalIgnoreCase)));

                            if (selectedAccount == null)
                            {
                                return BadRequest(new { error = "invalid_request", error_description = "Selected account is not available in this session" });
                            }

                            resolvedUserId = selectedAccount.UserId;
                            resolvedTenantId = selectedAccount.TenantId;
                        }
                        else if (filteredAccounts.Count > 1 || string.Equals(prompt, "select_account", StringComparison.OrdinalIgnoreCase))
                        {
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                            var chooserUrl = BuildSelectAccountUrl(
                                client_id,
                                response_type,
                                redirect_uri,
                                scope,
                                state,
                                nonce,
                                code_challenge,
                                code_challenge_method,
                                effectiveSessionId,
                                prompt,
                                filteredAccounts);

                            return Redirect(chooserUrl);
                        }
                        else if (filteredAccounts.Count == 1)
                        {
                            resolvedUserId = filteredAccounts[0].UserId;
                            resolvedTenantId = filteredAccounts[0].TenantId;
                            await _sessionRepo.UpdateActivityAsync(effectiveSessionId);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    resolvedUserId = claimUserId;
                    resolvedTenantId = claimTenantId;
                }

                if (string.IsNullOrWhiteSpace(resolvedUserId))
                {
                    _logger.LogInformation($"Unauthenticated authorization request for {client_id}");
                    return Redirect(BuildLoginUrl(client_id, response_type, redirect_uri, scope, state, nonce, code_challenge, code_challenge_method, tenant_id));
                }

                // Validate client
                var client = await _clientRepo.GetByClientIdAsync(client_id, resolvedTenantId);
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
                    TenantId = resolvedTenantId,
                    UserId = resolvedUserId,
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

                _logger.LogInformation($"Authorization code issued for user {resolvedUserId}, client {client_id}");

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
                else if (grant_type == "client_credentials")
                {
                    return await IssueClientCredentialsToken();
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

        private async Task<IActionResult> IssueClientCredentialsToken()
        {
            var clientId = Request.Form["client_id"].ToString();
            var clientSecret = Request.Form["client_secret"].ToString();

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                TryReadBasicClientAuthentication(out clientId, out clientSecret);
            }

            var organizationId = Request.Form["organization_id"].ToString();
            if (string.IsNullOrWhiteSpace(organizationId))
            {
                organizationId = Request.Form["org_id"].ToString();
            }

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            {
                return BadRequest(new { error = "invalid_client", error_description = "Missing client authentication" });
            }

            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            if (authConfiguration == null)
            {
                return BadRequest(new { error = "server_error", error_description = "Authentication configuration missing" });
            }

            var tokenRequest = new TokenRequest
            {
                GrantType = GrantTypes.ClientCredential,
                ClientId = clientId,
                ClientSecret = clientSecret,
                OrganizationId = organizationId,
                Request = Request
            };

            var result = await _clientCredentialAuthorizationService.AuthenticateAsync(tokenRequest, authConfiguration);
            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                var statusCode = string.Equals(result.Error, "invalid_client", StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.Status401Unauthorized
                    : StatusCodes.Status400BadRequest;

                return StatusCode(statusCode, new
                {
                    error = result.Error,
                    error_description = result.ErrorDescription
                });
            }

            return Ok(new TokenResponse
            {
                AccessToken = result.AccessToken,
                TokenType = "Bearer",
                ExpiresIn = result.ExpiresIn
            });
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
            var user = await _userRepository.GetUserByIdAsync(authCode.UserId);
            if (user == null)
            {
                return BadRequest(new { error = "invalid_grant", error_description = "User not found" });
            }

            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user,
                organizationId: null,
                authCode.Scope,
                client.AllowedScopes,
                requireExplicitScope: true);

            var claims = new OidcClaims
            {
                Sub = authCode.UserId,
                TenantId = authCode.TenantId ?? tenantId,
                Nonce = authCode.Nonce,
                AuthTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id,
                Audience = client.Audience ?? client_id,
                Scope = authCode.Scope,
                Roles = resolvedClaims.Roles,
                Resources = resolvedClaims.Resources,
                Permissions = resolvedClaims.Permissions
            };

            var issuer = $"https://{Request.Host}/";
            var idToken = await _tokenService.GenerateIdTokenAsync(claims, issuer, 3600);
            var accessToken = await _tokenService.GenerateAccessTokenAsync(claims, issuer, 3600);
            var refreshTokenModel = await _tokenService.GenerateRefreshTokenAsync(claims, issuer);

            // Store refresh token in DB
            refreshTokenModel.UserId = authCode.UserId;
            refreshTokenModel.ClientId = client_id;
            refreshTokenModel.TenantId = authCode.TenantId ?? tenantId;
            refreshTokenModel.Audience = client.Audience ?? client_id;
            refreshTokenModel.Scope = authCode.Scope;
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

            if (!string.IsNullOrWhiteSpace(storedToken.ClientId) && !string.Equals(storedToken.ClientId, client_id, StringComparison.Ordinal))
            {
                _logger.LogWarning($"Refresh token client mismatch. Presented client: {client_id}, token client: {storedToken.ClientId}");
                return BadRequest(new { error = "invalid_grant", error_description = "Refresh token does not belong to this client" });
            }

            // Generate new tokens with family tracking
            var user = await _userRepository.GetUserByIdAsync(storedToken.UserId);
            if (user == null)
            {
                return BadRequest(new { error = "invalid_grant", error_description = "User not found" });
            }

            var resolvedClaims = await _authorizationClaimsResolver.ResolveAsync(
                user,
                storedToken.OrgId,
                storedToken.Scope,
                client.AllowedScopes,
                requireExplicitScope: true);

            var claims = new OidcClaims
            {
                Sub = storedToken.UserId,
                TenantId = storedToken.TenantId,
                OrgId = storedToken.OrgId,
                Iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ClientId = client_id,
                Audience = client.Audience ?? client_id,
                Scope = storedToken.Scope,
                Roles = resolvedClaims.Roles,
                Resources = resolvedClaims.Resources,
                Permissions = resolvedClaims.Permissions
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
            newRefreshTokenModel.Audience = client.Audience ?? client_id;
            newRefreshTokenModel.Scope = storedToken.Scope;
            newRefreshTokenModel.SessionId = storedToken.SessionId;
            newRefreshTokenModel.IpAddress = GetClientIpAddress();
            newRefreshTokenModel.UserAgent = Request.Headers["User-Agent"].ToString();

            // Store new token
            await _refreshTokenRepo.CreateAsync(newRefreshTokenModel);

            // Revoke parent token after successful rotation (one-time use enforcement)
            storedToken.ChildTokenIds.Add(newRefreshTokenModel.TokenId);
            await _refreshTokenRepo.RevokeByTokenIdAsync(storedToken.TokenId, "rotated");

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

        private string BuildLoginUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string tenantId)
        {
            var loginUrl = new StringBuilder("/oidc/login?");
            loginUrl.Append($"client_id={Uri.EscapeDataString(clientId ?? string.Empty)}");
            loginUrl.Append($"&response_type={Uri.EscapeDataString(responseType ?? string.Empty)}");
            loginUrl.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri ?? string.Empty)}");
            loginUrl.Append($"&scope={Uri.EscapeDataString(scope ?? string.Empty)}");
            loginUrl.Append($"&state={Uri.EscapeDataString(state ?? string.Empty)}");
            loginUrl.Append($"&nonce={Uri.EscapeDataString(nonce ?? string.Empty)}");
            loginUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}");
            loginUrl.Append($"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod ?? string.Empty)}");

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                loginUrl.Append($"&tenant_id={Uri.EscapeDataString(tenantId)}");
            }

            return loginUrl.ToString();
        }

        private void TryReadBasicClientAuthentication(out string clientId, out string clientSecret)
        {
            clientId = string.Empty;
            clientSecret = string.Empty;

            if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var authHeader)
                || !string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(authHeader.Parameter))
            {
                return;
            }

            try
            {
                var rawCredentials = Encoding.UTF8.GetString(Convert.FromBase64String(authHeader.Parameter));
                var separatorIndex = rawCredentials.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    return;
                }

                clientId = rawCredentials[..separatorIndex];
                clientSecret = rawCredentials[(separatorIndex + 1)..];
            }
            catch
            {
                clientId = string.Empty;
                clientSecret = string.Empty;
            }
        }

        private string BuildSelectAccountUrl(
            string clientId,
            string responseType,
            string redirectUri,
            string scope,
            string state,
            string nonce,
            string codeChallenge,
            string codeChallengeMethod,
            string sessionId,
            string prompt,
            List<IdpSessionAccount> accounts)
        {
            var chooserUrl = new StringBuilder("/oidc/select-account?");
            chooserUrl.Append($"client_id={Uri.EscapeDataString(clientId ?? string.Empty)}");
            chooserUrl.Append($"&response_type={Uri.EscapeDataString(responseType ?? string.Empty)}");
            chooserUrl.Append($"&redirect_uri={Uri.EscapeDataString(redirectUri ?? string.Empty)}");
            chooserUrl.Append($"&scope={Uri.EscapeDataString(scope ?? string.Empty)}");
            chooserUrl.Append($"&state={Uri.EscapeDataString(state ?? string.Empty)}");
            chooserUrl.Append($"&nonce={Uri.EscapeDataString(nonce ?? string.Empty)}");
            chooserUrl.Append($"&code_challenge={Uri.EscapeDataString(codeChallenge ?? string.Empty)}");
            chooserUrl.Append($"&code_challenge_method={Uri.EscapeDataString(codeChallengeMethod ?? string.Empty)}");
            chooserUrl.Append($"&session_id={Uri.EscapeDataString(sessionId ?? string.Empty)}");

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                chooserUrl.Append($"&prompt={Uri.EscapeDataString(prompt)}");
            }

            foreach (var account in accounts)
            {
                var payload = $"{account.UserId}|{account.TenantId}|{account.DisplayName ?? string.Empty}";
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
                chooserUrl.Append($"&acct={Uri.EscapeDataString(encoded)}");
            }

            return chooserUrl.ToString();
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

