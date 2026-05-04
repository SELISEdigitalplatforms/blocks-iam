using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using DomainService.Services;

namespace DomainService.Oidc.Repositories
{
    /// <summary>
    /// Token Revocation Service Implementation
    /// Implements RFC 7009 (Token Revocation) and RFC 7662 (Token Introspection)
    /// </summary>
    public class TokenRevocationService : ITokenRevocationService
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

                var jti = ExtractJtiFromToken(token);
                if (string.IsNullOrWhiteSpace(jti))
                {
                    return new TokenRevocationResult
                    {
                        Success = false,
                        Error = "invalid_request",
                        ErrorDescription = "Token is invalid or malformed"
                    };
                }

                // Handle different token types
                if (tokenTypeHint == "refresh_token")
                {
                    // Revoke refresh token - this revokes the entire family
                    var refreshToken = await _refreshTokenRepo.GetByTokenIdAsync(token);
                    if (refreshToken != null && !refreshToken.IsRevoked)
                    {
                        await _refreshTokenRepo.RevokeByFamilyIdAsync(refreshToken.FamilyId, "user_revoked");
                        await SyncSessionStatusForRevokedFamilyAsync(refreshToken.FamilyId, refreshToken.UserId);
                        _logger.LogInformation($"Refresh token family revoked for token: {token}");
                    }
                }
                else
                {
                    // Revoke access token - add to blacklist
                    var expiresAt = ExtractExpiryFromToken(token);
                    var userId = ExtractUserIdFromToken(token);
                    
                    await _revocationRepo.RevokeTokenAsync(jti, userId, "user_revoked", expiresAt);
                    _logger.LogInformation($"Access token revoked: {jti}");
                }

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
                var token_obj = handler.ReadJwtToken(token);

                var expiryTime = token_obj.ValidTo;
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
                    Sub = token_obj.Subject,
                    ClientId = token_obj.Audiences.FirstOrDefault(),
                    Iss = token_obj.Issuer,
                    Iat = new DateTimeOffset(token_obj.IssuedAt).ToUnixTimeSeconds(),
                    Exp = new DateTimeOffset(expiryTime).ToUnixTimeSeconds(),
                    TokenType = "Bearer",
                    Scope = ExtractScopeFromToken(token_obj)
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
                var familyIds = new HashSet<string>();

                // Collect all family IDs
                foreach (var token in userTokens)
                {
                    if (!string.IsNullOrWhiteSpace(token.FamilyId))
                    {
                        familyIds.Add(token.FamilyId);
                    }
                }

                // Revoke all families
                foreach (var familyId in familyIds)
                {
                    await _refreshTokenRepo.RevokeByFamilyIdAsync(familyId, reason);
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

                // Revoke all families for this client
                var familyIds = clientTokens.Select(t => t.FamilyId).Distinct();
                foreach (var familyId in familyIds)
                {
                    await _refreshTokenRepo.RevokeByFamilyIdAsync(familyId, reason);
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

        private string? ExtractUserIdFromToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                return jwtToken.Subject;
            }
            catch
            {
                return null;
            }
        }

        private DateTime ExtractExpiryFromToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);
                return jwtToken.ValidTo;
            }
            catch
            {
                return DateTime.UtcNow.AddDays(30); // Default 30 days
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

        private async Task SyncSessionStatusForRevokedFamilyAsync(string familyId, string userId)
        {
            if (string.IsNullOrWhiteSpace(familyId) || string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var familyTokens = await _refreshTokenRepo.GetByFamilyIdAsync(familyId);
            await SyncSessionStatusForRefreshTokensAsync(familyTokens.Select(t => t.TokenId));
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

