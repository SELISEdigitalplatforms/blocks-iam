using Blocks.Genesis;
using Iam.DomainService.Services;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Blocks.Genesis.Entities.Iam;

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
        private readonly IIamDataAccess _dataAccess;

        public TokenRevocationRepository(
            IDbContextProvider dbContextProvider,
            ILogger<TokenRevocationRepository> logger,
            IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
            _dataAccess = dataAccess;
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
            await _dataAccess.InsertOneAsync(collection, IamSchemas.TokenRevocationModel, model, "TokenRevocationRepository.RevokeTokenAsync");
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
            var scope = await _dataAccess.ReadAsync(IamSchemas.TokenRevocationModel, filter, "TokenRevocationRepository.IsRevokedAsync", useProjection: false);
            // Denied read = fail closed: the token is treated as revoked rather than silently valid.
            if (!scope.Granted) return true;
            var result = await scope.Apply(collection).FirstOrDefaultAsync();
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

            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.TokenRevocationModel, filter, update, "TokenRevocationRepository.DeleteAsync");
            return result?.ModifiedCount > 0;
        }

        /// <summary>
        /// Get revocation details for introspection
        /// </summary>
        public async Task<TokenRevocationModel> GetRevocationDetailsAsync(string jti)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.Jti, jti);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.TokenRevocationModel, filter, "TokenRevocationRepository.GetRevocationDetailsAsync");
        }

        /// <summary>
        /// Get all revoked tokens for audit purposes
        /// </summary>
        public async Task<IEnumerable<TokenRevocationModel>> GetRevokedTokensByUserAsync(string userId)
        {
            var collection = GetDatabase().GetCollection<TokenRevocationModel>("IdpRevokedTokens");
            var filter = Builders<TokenRevocationModel>.Filter.Eq(t => t.UserId, userId);
            var scope = await _dataAccess.ReadAsync(IamSchemas.TokenRevocationModel, filter, "TokenRevocationRepository.GetRevokedTokensByUserAsync", useProjection: false);
            if (!scope.Granted) return [];
            return await scope.Apply(collection).SortByDescending(t => t.RevokedAt).ToListAsync();
        }

        public async Task<IEnumerable<TokenRevocationModel>> GetByUserAsync(string userId)
        {
            return await GetRevokedTokensByUserAsync(userId);
        }
    }
}