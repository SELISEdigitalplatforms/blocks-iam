using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MongoDB.Driver;
using Blocks.Genesis;
using Blocks.Genesis.Auth;
using Microsoft.Extensions.Logging;

namespace DomainService.Oidc.Repositories
{
    public class IdpSessionRepository : IIdpSessionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<IdpSessionRepository> _logger;

        public IdpSessionRepository(
            IDbContextProvider dbContextProvider,
            ILogger<IdpSessionRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(IdpSessionModel session)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                await collection.InsertOneAsync(session);
                _logger.LogInformation($"IdP session created: {session.SessionId}");
                return session.SessionId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating IdP session");
                throw;
            }
        }

        public async Task<IdpSessionModel> GetBySessionIdAsync(string sessionId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching IdP session: {sessionId}");
                throw;
            }
        }

        public async Task<bool> AddAccountAsync(string sessionId, IdpSessionAccount account)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                var update = Builders<IdpSessionModel>.Update
                    .Push(s => s.Accounts, account)
                    .Set(s => s.LastActivityAt, DateTime.UtcNow)
                    .Set(s => s.IdleExpiry, DateTime.UtcNow.AddHours(24));
                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding account to IdP session: {sessionId}");
                throw;
            }
        }

        public async Task<bool> RemoveAccountAsync(string sessionId, string userId, string tenantId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                var update = Builders<IdpSessionModel>.Update
                    .PullFilter(s => s.Accounts, a => a.UserId == userId && a.TenantId == tenantId)
                    .Set(s => s.LastActivityAt, DateTime.UtcNow)
                    .Set(s => s.IdleExpiry, DateTime.UtcNow.AddHours(24));
                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing account from IdP session: {sessionId}");
                throw;
            }
        }

        public async Task<bool> UpdateActivityAsync(string sessionId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                var update = Builders<IdpSessionModel>.Update
                    .Set(s => s.LastActivityAt, DateTime.UtcNow)
                    .Set(s => s.IdleExpiry, DateTime.UtcNow.AddHours(24));
                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating IdP session activity: {sessionId}");
                throw;
            }
        }

        public async Task<bool> RevokeAsync(string sessionId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                var update = Builders<IdpSessionModel>.Update.Set(s => s.RevokedAt, DateTime.UtcNow);
                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error revoking IdP session: {sessionId}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string sessionId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
                var result = await collection.DeleteOneAsync(filter);
                return result.DeletedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting IdP session: {sessionId}");
                throw;
            }
        }

        public async Task<IEnumerable<IdpSessionModel>> GetByUserAsync(string userId, string tenantId)
        {
            try
            {
                var collection = GetDatabase().GetCollection<IdpSessionModel>("idp_sessions");
                var filter = Builders<IdpSessionModel>.Filter.And(
                    Builders<IdpSessionModel>.Filter.ElemMatch(s => s.Accounts, 
                        Builders<IdpSessionAccount>.Filter.Eq(a => a.UserId, userId)),
                    Builders<IdpSessionModel>.Filter.Eq(s => s.TenantId, tenantId)
                );
                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching IdP sessions for user: {userId}");
                throw;
            }
        }
    }
}

