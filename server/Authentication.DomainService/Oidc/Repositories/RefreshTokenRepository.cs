using MongoDB.Driver;
using Blocks.Genesis;
using Authentication.DomainService.Entities;
using Authentication.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Repositories
{
    public sealed class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly IAuthenticationRepository _authenticationRepository;
        private readonly ILogger<RefreshTokenRepository> _logger;

        public RefreshTokenRepository(
            IDbContextProvider dbContextProvider,
            IAuthenticationRepository authenticationRepository,
            ILogger<RefreshTokenRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _authenticationRepository = authenticationRepository;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(RefreshTokenModel token)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            await collection.InsertOneAsync(token);
            _logger.LogInformation("Refresh token created for user {UserId}, client {ClientId}", token.UserId, token.ClientId);
            return token.TokenId;
        }

        public async Task<RefreshTokenModel> GetByTokenIdAsync(string tokenId)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetByUserAsync(string userId, string tenantId)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.And(
                Builders<RefreshTokenModel>.Filter.Eq(t => t.UserId, userId),
                Builders<RefreshTokenModel>.Filter.Eq(t => t.TenantId, tenantId)
            );
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetBySessionIdAsync(string sessionId)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.SessionId, sessionId);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetRotationHistoryAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return [];
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.SessionId, sessionId);
            return await collection.Find(filter)
                .SortBy(t => t.AbsoluteExpiry)
                .ToListAsync();
        }

        public async Task<bool> RevokeByTokenIdAsync(string tokenId, string reason)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> UpdateSlidingExpiryAsync(string tokenId)
        {
            var authConfiguration = await _authenticationRepository.GetAuthenticationConfigurationAsync();
            var slidingMinutes = Math.Max(authConfiguration?.RefreshTokenValidForNumberMinutes ?? IdentityConfiguration.DefaultRefreshTokenValidForNumberMinutes, 1);
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.SlidingExpiry, DateTime.UtcNow.AddMinutes(slidingMinutes));

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteAsync(string tokenId)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetExpiredAsync()
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Lt(t => t.AbsoluteExpiry, DateTime.UtcNow);
            return await collection.Find(filter).ToListAsync();
        }
    }
}