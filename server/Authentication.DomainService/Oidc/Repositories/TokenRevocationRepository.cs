using Idp.DomainService.Oidc.Contracts;
using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Token Revocation Repository
    /// Implements RFC 7009 Token Revocation and RFC 7662 Token Introspection
    /// Maintains JTI (JWT ID) blacklist for immediate token revocation
    /// </summary>
    public class TokenRevocationRepository : ITokenRevocationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<TokenRevocationRepository> _logger;

        public TokenRevocationRepository(
            IDbContextProvider dbContextProvider,
            ILogger<TokenRevocationRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        /// <summary>
        /// Revoke a specific token by JTI (RFC 7009)
        /// </summary>
        public async Task<bool> RevokeTokenAsync(string jti, string userId, string reason, DateTime expiresAt)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var model = new TokenRevocationModel
                {
                    Jti = jti,
                    UserId = userId,
                    RevokedAt = DateTime.UtcNow,
                    RevokeReason = reason,
                    ExpiresAt = expiresAt
                };
                await collection.InsertOneAsync(model);
                _logger.LogInformation($"Token revoked: {jti}, reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking token: {jti}");
                throw;
            }
        }

        /// <summary>
        /// Check if token is revoked (RFC 7009)
        /// </summary>
        public async Task<bool> IsRevokedAsync(string jti)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
                var result = await collection.Find(filter).FirstOrDefaultAsync();
                return result != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking token revocation: {jti}");
                throw;
            }
        }

        /// <summary>
        /// Revoke entire token family by FamilyId
        /// Used when token reuse is detected
        /// </summary>
        public async Task<bool> RevokeTokenFamilyAsync(string familyId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.FamilyId, familyId);
                var update = Builders<TokenRevocationModel>.Update
                    .Set(t => t.RevokeReason, "family_revoked_for_reuse_detection");
                
                var result = await collection.UpdateManyAsync(filter, update);
                _logger.LogCritical($"Token family revoked: {familyId}, tokens affected: {result.ModifiedCount}");
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking token family: {familyId}");
                throw;
            }
        }

        /// <summary>
        /// Check if token family is revoked
        /// </summary>
        public async Task<bool> IsTokenFamilyRevokedAsync(string familyId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.And(
                    Builders<TokenRevocationModel>.Filter.Eq(t => t.FamilyId, familyId),
                    Builders<TokenRevocationModel>.Filter.Eq(t => t.RevokeReason, "family_revoked_for_reuse_detection")
                );
                var result = await collection.Find(filter).FirstOrDefaultAsync();
                return result != null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking family revocation: {familyId}");
                throw;
            }
        }

        /// <summary>
        /// Delete revocation record
        /// </summary>
        public async Task<bool> DeleteAsync(string jti)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
                var result = await collection.DeleteOneAsync(filter);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting revocation record: {jti}");
                throw;
            }
        }

        /// <summary>
        /// Get revocation details for introspection
        /// </summary>
        public async Task<TokenRevocationModel> GetRevocationDetailsAsync(string jti)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching revocation details: {jti}");
                throw;
            }
        }

        /// <summary>
        /// Get all revoked tokens for audit purposes
        /// </summary>
        public async Task<IEnumerable<TokenRevocationModel>> GetRevokedTokensByUserAsync(string userId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
                var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.UserId, userId);
                return await collection.Find(filter).SortByDescending(t => t.RevokedAt).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching revoked tokens for user: {userId}");
                throw;
            }
        }
    }

    public class TokenRevocationModel
    {
        public string? Id { get; set; } // MongoDB ObjectId
        public string? Jti { get; set; } // JWT ID (unique token identifier)
        public string? UserId { get; set; }
        public string? FamilyId { get; set; } // For family revocation
        public DateTime RevokedAt { get; set; }
        public string? RevokeReason { get; set; } // "user_revoked", "logout", "reuse_detected", "password_changed"
        public DateTime ExpiresAt { get; set; } // After this date, can be deleted from DB
    }
}

