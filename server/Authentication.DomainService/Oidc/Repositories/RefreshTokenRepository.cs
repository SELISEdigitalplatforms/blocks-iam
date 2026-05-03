using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Blocks.Genesis;
using Blocks.Genesis.Auth;
using Microsoft.Extensions.Logging;

namespace DomainService.Oidc.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<RefreshTokenRepository> _logger;

        public RefreshTokenRepository(
            IDbContextProvider dbContextProvider,
            ILogger<RefreshTokenRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(RefreshTokenModel token)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                await collection.InsertOneAsync(token);
                _logger.LogInformation($"Refresh token created for user {token.UserId}, client {token.ClientId}, family {token.FamilyId}");
                return token.TokenId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating refresh token");
                throw;
            }
        }

        public async Task<RefreshTokenModel> GetByTokenIdAsync(string tokenId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching refresh token: {tokenId}");
                throw;
            }
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetByUserAsync(string userId, string tenantId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.And(
                    Builders<RefreshTokenModel>.Filter.Eq(t => t.UserId, userId),
                    Builders<RefreshTokenModel>.Filter.Eq(t => t.TenantId, tenantId)
                );
                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching refresh tokens for user: {userId}");
                throw;
            }
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetByFamilyIdAsync(string familyId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.FamilyId, familyId);
                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching token family: {familyId}");
                throw;
            }
        }

        public async Task<bool> RevokeByTokenIdAsync(string tokenId, string reason)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
                var update = Builders<RefreshTokenModel>.Update
                    .Set(t => t.IsRevoked, true)
                    .Set(t => t.RevokeReason, reason)
                    .Set(t => t.RevokedAt, DateTime.UtcNow);

                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking refresh token: {tokenId}");
                throw;
            }
        }

        public async Task<bool> RevokeByFamilyIdAsync(string familyId, string reason)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.FamilyId, familyId);
                var update = Builders<RefreshTokenModel>.Update
                    .Set(t => t.IsRevoked, true)
                    .Set(t => t.RevokeReason, reason)
                    .Set(t => t.RevokedAt, DateTime.UtcNow);

                var result = await collection.UpdateManyAsync(filter, update);
                _logger.LogCritical($"Token family revoked: {familyId}, reason: {reason}, tokens affected: {result.ModifiedCount}");
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking token family: {familyId}");
                throw;
            }
        }

        public async Task<bool> UpdateSlidingExpiryAsync(string tokenId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
                var update = Builders<RefreshTokenModel>.Update
                    .Set(t => t.SlidingExpiry, DateTime.UtcNow.AddHours(24));

                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating sliding expiry: {tokenId}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string tokenId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Eq(t => t.TokenId, tokenId);
                var result = await collection.DeleteOneAsync(filter);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting refresh token: {tokenId}");
                throw;
            }
        }

        public async Task<IEnumerable<RefreshTokenModel>> GetExpiredAsync()
        {
            try
            {
                var collection = GetDatabase().GetCollection<RefreshTokenModel>("refresh_tokens");
                var filter = Builders<RefreshTokenModel>.Filter.Lt(t => t.AbsoluteExpiry, DateTime.UtcNow);
                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching expired refresh tokens");
                throw;
            }
        }
    }
}

