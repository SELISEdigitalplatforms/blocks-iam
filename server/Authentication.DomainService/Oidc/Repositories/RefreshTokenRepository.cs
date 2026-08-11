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
        private readonly ICacheClient _cacheClient;
        private readonly ILogger<RefreshTokenRepository> _logger;

        public RefreshTokenRepository(
            IDbContextProvider dbContextProvider,
            ICacheClient cacheClient,
            ILogger<RefreshTokenRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _cacheClient = cacheClient;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(RefreshTokenModel token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }
            if (string.IsNullOrWhiteSpace(token.TokenId))
            {
                throw new ArgumentException("RefreshTokenModel.TokenId is required.", nameof(token));
            }
            if (string.IsNullOrWhiteSpace(token.UserId))
            {
                throw new ArgumentException("RefreshTokenModel.UserId is required.", nameof(token));
            }
            if (string.IsNullOrWhiteSpace(token.TenantId))
            {
                throw new ArgumentException("RefreshTokenModel.TenantId is required.", nameof(token));
            }

            if (string.IsNullOrWhiteSpace(token.SessionId))
            {
                throw new ArgumentException("RefreshTokenModel.SessionId is required.", nameof(token));
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            await collection.InsertOneAsync(token);
            _logger.LogInformation("Refresh token created for user {UserId}, client {ClientId}, session {SessionId}", token.UserId, token.ClientId, token.SessionId);
            return token.TokenId;
        }

        public async Task<RefreshTokenModel> GetByTokenIdAsync(string tokenId)
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return null;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IReadOnlyList<RefreshTokenModel>> GetBySessionIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Array.Empty<RefreshTokenModel>();
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.SessionId, sessionId);
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<IReadOnlyList<RefreshTokenModel>> GetActiveTokensBySessionIdAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Array.Empty<RefreshTokenModel>();
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var now = DateTime.UtcNow;
            var filter = Builders<RefreshTokenModel>.Filter.And(
                Builders<RefreshTokenModel>.Filter.Eq(t => t.SessionId, sessionId),
                Builders<RefreshTokenModel>.Filter.Eq(t => t.IsRevoked, false),
                Builders<RefreshTokenModel>.Filter.Gt(t => t.AbsoluteExpiry, now),
                Builders<RefreshTokenModel>.Filter.Gt(t => t.SlidingExpiry, now)
            );
            return await collection.Find(filter)
                .SortByDescending(t => t.IssuedUtc)
                .ToListAsync();
        }

        public async Task<IReadOnlyList<RefreshTokenModel>> GetActiveTokensByUserAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Array.Empty<RefreshTokenModel>();
            }

            var now = DateTime.UtcNow;
            var b = Builders<RefreshTokenModel>.Filter;
            var filter = b.And(
                b.Eq(t => t.UserId, userId),
                b.Eq(t => t.IsRevoked, false),
                b.Gt(t => t.AbsoluteExpiry, now),
                b.Gt(t => t.SlidingExpiry, now)
            );

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
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

        public async Task<IEnumerable<RefreshTokenModel>> GetByUserAsync(string userId, string tenantId)
        {
            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.And(
                Builders<RefreshTokenModel>.Filter.Eq(t => t.UserId, userId),
                Builders<RefreshTokenModel>.Filter.Eq(t => t.TenantId, tenantId)
            );
            return await collection.Find(filter).ToListAsync();
        }

        public async Task<bool> RevokeByTokenIdAsync(string tokenId, string reason, string? supersededByTokenId = null)
        {
            if (string.IsNullOrWhiteSpace(tokenId))
            {
                return false;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            // The successor pointer is what makes the rotation replayable inside the grace window, so it
            // has to land in the same update that marks the predecessor revoked.
            if (!string.IsNullOrWhiteSpace(supersededByTokenId))
            {
                update = update.Set(t => t.SupersededByTokenId, supersededByTokenId);
            }

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<int> RevokeAllByRefreshTokenSessionIdAsync(string refreshTokenSessionId, string reason)
        {
            if (string.IsNullOrWhiteSpace(refreshTokenSessionId))
            {
                return 0;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var b = Builders<RefreshTokenModel>.Filter;

            // A pre-lineage document is a lineage of one keyed by its own TokenId, so a lineage id that
            // matches a token id must revoke that token too.
            var filter = b.And(
                b.Or(
                    b.Eq(t => t.RefreshTokenSessionId, refreshTokenSessionId),
                    b.And(
                        b.Eq(t => t.TokenId, refreshTokenSessionId),
                        b.Or(
                            b.Eq(t => t.RefreshTokenSessionId, null),
                            b.Exists(t => t.RefreshTokenSessionId, false)))),
                b.Eq(t => t.IsRevoked, false));

            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            return (int)result.ModifiedCount;
        }

        public async Task<int> RevokeSupersededLoginLineagesAsync(
            string sessionId,
            string userId,
            string clientId,
            string exceptRefreshTokenSessionId,
            string reason)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(clientId))
            {
                return 0;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var b = Builders<RefreshTokenModel>.Filter;
            var now = DateTime.UtcNow;

            // Pinning SessionId + UserId + ClientId is what keeps a second account, another application
            // and another device out of the blast radius. Both clocks are required so an already-dead
            // lineage is not rewritten and its original RevokeReason preserved.
            var filter = b.And(
                b.Eq(t => t.SessionId, sessionId),
                b.Eq(t => t.UserId, userId),
                b.Eq(t => t.ClientId, clientId),
                b.Eq(t => t.IsRevoked, false),
                b.Gt(t => t.AbsoluteExpiry, now),
                b.Gt(t => t.SlidingExpiry, now),
                b.Ne(t => t.RefreshTokenSessionId, exceptRefreshTokenSessionId),
                b.Ne(t => t.TokenId, exceptRefreshTokenSessionId));

            var candidates = await collection.Find(filter).ToListAsync();
            if (candidates.Count == 0)
            {
                return 0;
            }

            var tokenIds = candidates
                .Select(t => t.TokenId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList();

            foreach (var tokenId in tokenIds)
            {
                await _cacheClient.RemoveKeyAsync(tokenId);
            }

            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(
                Builders<RefreshTokenModel>.Filter.In(t => t.TokenId, tokenIds),
                update);

            return (int)result.ModifiedCount;
        }

        public async Task<int> RevokeAllByTokenIdsAsync(IEnumerable<string> tokenIds, string reason)
        {
            var ids = tokenIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
            if (ids == null || ids.Count == 0)
            {
                return 0;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.In(t => t.TokenId, ids);
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            return (int)result.ModifiedCount;
        }

        public async Task<int> RevokeAllBySessionIdAsync(string sessionId, string reason)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return 0;
            }

            var collection = GetDatabase().GetCollection<RefreshTokenModel>("IdpRefreshTokens");
            var filter = Builders<RefreshTokenModel>.Filter.And(
                Builders<RefreshTokenModel>.Filter.Eq(t => t.SessionId, sessionId),
                Builders<RefreshTokenModel>.Filter.Eq(t => t.IsRevoked, false)
            );
            var update = Builders<RefreshTokenModel>.Update
                .Set(t => t.IsRevoked, true)
                .Set(t => t.RevokeReason, reason)
                .Set(t => t.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateManyAsync(filter, update);
            return (int)result.ModifiedCount;
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
