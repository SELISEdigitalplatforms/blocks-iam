using Authentication.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using System.IdentityModel.Tokens.Jwt;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Token Revocation Service Implementation
    /// Implements RFC 7009 (Token Revocation) and RFC 7662 (Token Introspection)
    /// </summary>
    public sealed class TokenRevocationService : ITokenRevocationService
    {
        private readonly ITokenRevocationRepository _revocationRepo;
        private readonly IRefreshTokenRepository _refreshTokenRepo;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<TokenRevocationService> _logger;

        public TokenRevocationService(
            ITokenRevocationRepository revocationRepo,
            IRefreshTokenRepository refreshTokenRepo,
            IAuthenticationRepository authenticationRepository,
            ILogger<TokenRevocationService> logger)
        {
            _revocationRepo = revocationRepo;
            _refreshTokenRepo = refreshTokenRepo;
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        /// <summary>
        /// RFC 7009: Revoke a token
        /// Supports both access_token and refresh_token revocation
        /// </summary>
        public async Task<TokenRevocationResult> RevokeTokenAsync(string token, string tokenTypeHint, string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new TokenRevocationResult
                    {
                        Success = false,
                        Error = "invalid_request",
                        ErrorDescription = "Token parameter is required"
                    };
                }

                // RFC 7009: unknown tokens should still return success to avoid token enumeration.
                // Try refresh token revocation first for explicit/implicit refresh_token flows.
                var isRefreshHint = string.Equals(tokenTypeHint, "refresh_token", StringComparison.OrdinalIgnoreCase);
                if (isRefreshHint || string.IsNullOrWhiteSpace(tokenTypeHint))
                {
                    var refreshToken = await _refreshTokenRepo.GetByTokenIdAsync(token);
                    if (refreshToken != null)
                    {
                        if (!IsClientAuthorizedForRefreshToken(refreshToken, clientId))
                        {
                            return new TokenRevocationResult
                            {
                                Success = false,
                                Error = "invalid_client",
                                ErrorDescription = "client_id is not authorized for this token"
                            };
                        }

                        if (!refreshToken.IsRevoked)
                        {
                            await _refreshTokenRepo.RevokeByTokenIdAsync(refreshToken.TokenId, "user_revoked");
                            await SyncSessionStatusForRefreshTokensAsync(new[] { refreshToken.TokenId });
                            _logger.LogInformation("Refresh token revoked for token id: {TokenId}", token);
                        }

                        return new TokenRevocationResult { Success = true };
                    }

                    if (isRefreshHint)
                    {
                        return new TokenRevocationResult { Success = true };
                    }
                }

                var jti = ExtractJtiFromToken(token);
                if (string.IsNullOrWhiteSpace(jti))
                {
                    return new TokenRevocationResult { Success = true };
                }

                var handler = new JwtSecurityTokenHandler();
                var tokenObj = handler.ReadJwtToken(token);
                if (!IsClientAuthorizedForAccessToken(tokenObj, clientId))
                {
                    return new TokenRevocationResult
                    {
                        Success = false,
                        Error = "invalid_client",
                        ErrorDescription = "client_id is not authorized for this token"
                    };
                }

                // Revoke access token - add to blacklist
                var expiresAt = tokenObj.ValidTo == default ? DateTime.UtcNow.AddMinutes(30) : tokenObj.ValidTo;
                var userId = tokenObj.Subject;
                if (string.IsNullOrWhiteSpace(userId))
                {
                    userId = string.Empty;
                }

                await _revocationRepo.RevokeTokenAsync(jti, userId, "user_revoked", expiresAt);
                _logger.LogInformation("Access token revoked: {Jti}", jti);

                return new TokenRevocationResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking token");
                return new TokenRevocationResult
                {
                    Success = false,
                    Error = "server_error",
                    ErrorDescription = "An error occurred processing the revocation request"
                };
            }
        }

        /// <summary>
        /// RFC 7662: Introspect a token to get active status and claims
        /// Returns token metadata if valid and active
        /// </summary>
        public async Task<TokenIntrospectionResult> IntrospectTokenAsync(string token, string tokenTypeHint, string clientId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new TokenIntrospectionResult
                    {
                        Active = false,
                        Error = "invalid_request",
                        ErrorDescription = "Token parameter is required"
                    };
                }

                var jti = ExtractJtiFromToken(token);
                if (string.IsNullOrWhiteSpace(jti))
                {
                    return new TokenIntrospectionResult
                    {
                        Active = false
                    };
                }

                // Check if token is revoked
                var isRevoked = await _revocationRepo.IsRevokedAsync(jti);
                if (isRevoked)
                {
                    _logger.LogInformation($"Introspection request for revoked token: {jti}");
                    return new TokenIntrospectionResult
                    {
                        Active = false
                    };
                }

                // Extract claims from token
                var handler = new JwtSecurityTokenHandler();
                var tokenObj = handler.ReadJwtToken(token);

                if (!IsClientAuthorizedForAccessToken(tokenObj, clientId))
                {
                    return new TokenIntrospectionResult
                    {
                        Active = false
                    };
                }

                var expiryTime = tokenObj.ValidTo;
                var isExpired = expiryTime < DateTime.UtcNow;

                if (isExpired)
                {
                    _logger.LogInformation($"Introspection request for expired token: {jti}");
                    return new TokenIntrospectionResult
                    {
                        Active = false,
                        Exp = new DateTimeOffset(expiryTime).ToUnixTimeSeconds()
                    };
                }

                // Return active token details
                return new TokenIntrospectionResult
                {
                    Active = true,
                    Jti = jti,
                    Sub = tokenObj.Subject,
                    ClientId = tokenObj.Audiences.FirstOrDefault(),
                    Iss = tokenObj.Issuer,
                    Iat = new DateTimeOffset(tokenObj.IssuedAt).ToUnixTimeSeconds(),
                    Exp = new DateTimeOffset(expiryTime).ToUnixTimeSeconds(),
                    TokenType = "Bearer",
                    Scope = ExtractScopeFromToken(tokenObj)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error introspecting token");
                return new TokenIntrospectionResult
                {
                    Active = false,
                    Error = "server_error",
                    ErrorDescription = "An error occurred processing the introspection request"
                };
            }
        }

        /// <summary>
        /// Revoke all tokens for a user
        /// Called on logout, password change, account deletion
        /// </summary>
        public async Task<bool> RevokeAllUserTokensAsync(string userId, string tenantId, string reason)
        {
            try
            {
                var userTokens = await _refreshTokenRepo.GetByUserAsync(userId, tenantId);
                var activeTokens = userTokens.Where(t => !t.IsRevoked).ToList();

                foreach (var token in activeTokens)
                {
                    await _refreshTokenRepo.RevokeByTokenIdAsync(token.TokenId, reason);
                }

                await SyncSessionStatusForRefreshTokensAsync(userTokens.Select(t => t.TokenId));

                _logger.LogInformation($"All tokens revoked for user {userId}, reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking all user tokens: {userId}");
                throw;
            }
        }

        /// <summary>
        /// Revoke all tokens for a user-client combination
        /// </summary>
        public async Task<bool> RevokeUserClientTokensAsync(string userId, string clientId, string reason)
        {
            try
            {
                // Get user tokens for this client
                var userTokens = await _refreshTokenRepo.GetByUserAsync(userId, ""); // TODO: Get tenantId from context
                var clientTokens = userTokens.Where(t => t.ClientId == clientId && !t.IsRevoked).ToList();

                foreach (var token in clientTokens)
                {
                    await _refreshTokenRepo.RevokeByTokenIdAsync(token.TokenId, reason);
                }

                await SyncSessionStatusForRefreshTokensAsync(clientTokens.Select(t => t.TokenId));

                _logger.LogInformation($"All tokens revoked for user {userId} on client {clientId}, reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking user-client tokens: {userId}, {clientId}");
                throw;
            }
        }

        /// <summary>
        /// Get revocation history for audit trail
        /// </summary>
        public async Task<IEnumerable<TokenRevocationModel>> GetRevocationHistoryAsync(string userId)
        {
            try
            {
                return await _revocationRepo.GetRevokedTokensByUserAsync(userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching revocation history: {userId}");
                throw;
            }
        }

        private static bool IsClientAuthorizedForRefreshToken(RefreshTokenModel refreshToken, string clientId)
        {
            // clientId is required for authorization
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            return string.Equals(refreshToken.ClientId, clientId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsClientAuthorizedForAccessToken(JwtSecurityToken token, string clientId)
        {
            // clientId is required for authorization
            if (string.IsNullOrWhiteSpace(clientId))
            {
                return false;
            }

            return token.Audiences.Any(aud => string.Equals(aud, clientId, StringComparison.OrdinalIgnoreCase));
        }

        // Helper methods
        private string? ExtractJtiFromToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                return jwtToken.Id;
            }
            catch
            {
                return null;
            }
        }

        private string ExtractScopeFromToken(JwtSecurityToken token)
        {
            try
            {
                var scopeClaim = token.Claims.FirstOrDefault(c => c.Type == "scope");
                return scopeClaim?.Value ?? "";
            }
            catch
            {
                return "";
            }
        }

        private async Task SyncSessionStatusForRefreshTokensAsync(IEnumerable<string> refreshTokenIds)
        {
            var tokenIds = refreshTokenIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (tokenIds.Count == 0)
            {
                return;
            }

            await _authenticationRepository.UpdateSessionStatusForAllRefreshTokenAsync(tokenIds);
        }
    }
}

