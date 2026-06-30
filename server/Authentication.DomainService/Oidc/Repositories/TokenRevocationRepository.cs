using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Token Revocation Repository
    /// Implements RFC 7009 Token Revocation and RFC 7662 Token Introspection
    /// Maintains JTI (JWT ID) blacklist for immediate token revocation
    /// </summary>
    public sealed class TokenRevocationRepository : ITokenRevocationRepository
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
            _logger.LogInformation("Token revoked: {Jti}, reason: {Reason}", jti, reason);
            return true;
        }

        /// <summary>
        /// Check if token is revoked (RFC 7009)
        /// </summary>
        public async Task<bool> IsRevokedAsync(string jti)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
            var result = await collection.Find(filter).FirstOrDefaultAsync();
            return result != null;
        }

        /// <summary>
        /// Delete revocation record
        /// </summary>
        public async Task<bool> DeleteAsync(string jti)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
            var update = Builders<TokenRevocationModel>.Update
                .Set(t => t.IsDeleted, true)
                .Set(t => t.DeletedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        /// <summary>
        /// Get revocation details for introspection
        /// </summary>
        public async Task<TokenRevocationModel> GetRevocationDetailsAsync(string jti)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        /// <summary>
        /// Get all revoked tokens for audit purposes
        /// </summary>
        public async Task<IEnumerable<TokenRevocationModel>> GetRevokedTokensByUserAsync(string userId)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.UserId, userId);
            return await collection.Find(filter).SortByDescending(t => t.RevokedAt).ToListAsync();
        }
    }

    public sealed class TokenRevocationModel
    {
        public string? Id { get; set; } // MongoDB ObjectId
        public string? Jti { get; set; } // JWT ID (unique token identifier)
        public string? UserId { get; set; }
        public DateTime RevokedAt { get; set; }
        public string? RevokeReason { get; set; } // "user_revoked", "logout", "reuse_detected", "password_changed"
        public DateTime ExpiresAt { get; set; } // After this date, can be deleted from DB
        public bool IsDeleted { get; set; } // Soft delete flag
        public DateTime DeletedAt { get; set; } // Soft delete timestamp
    }
}