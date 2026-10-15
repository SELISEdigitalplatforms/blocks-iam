using MongoDB.Driver;
using Authentication.DomainService.Utilities;
using Authentication.DomainService.Shared;
using Iam.DomainService.Utilities;
using Blocks.Genesis;
using Iam.DomainService.Services;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using Blocks.Genesis.Entities.Iam;

namespace Authentication.DomainService.Oidc.Repositories
{
    public sealed class IdpSessionRepository : IIdpSessionRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<IdpSessionRepository> _logger;
        private readonly IIamDataAccess _dataAccess;

        public IdpSessionRepository(
            IDbContextProvider dbContextProvider,
            ILogger<IdpSessionRepository> logger,
            IIamDataAccess dataAccess)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
            _dataAccess = dataAccess;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(IdpSessionModel session)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            await _dataAccess.InsertOneAsync(collection, IamSchemas.IdpSessionModel, session, "IdpSessionRepository.CreateAsync");
            _logger.LogInformation("IdP session created: {SessionId}", session.SessionId);
            return session.SessionId;
        }

        public async Task<IdpSessionModel> GetBySessionIdAsync(string sessionId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            return await _dataAccess.FindFirstAsync(collection, IamSchemas.IdpSessionModel, filter, "IdpSessionRepository.GetBySessionIdAsync");
        }

        public async Task<bool> AddAccountAsync(string sessionId, IdpSessionAccount account)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            var idleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout());
            var update = Builders<IdpSessionModel>.Update
                .Push(s => s.Accounts, account)
                .Set(s => s.LastActivityAt, DateTime.UtcNow)
                .Set(s => s.IdleExpiry, idleExpiry);
            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.IdpSessionModel, filter, update, "IdpSessionRepository.AddAccountAsync");
            return result?.ModifiedCount > 0;
        }

        public async Task<bool> RemoveAccountAsync(string sessionId, string userId, string tenantId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            var idleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout());
            var update = Builders<IdpSessionModel>.Update
                .PullFilter(s => s.Accounts, a => a.UserId == userId && a.TenantId == tenantId)
                .Set(s => s.LastActivityAt, DateTime.UtcNow)
                .Set(s => s.IdleExpiry, idleExpiry);
            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.IdpSessionModel, filter, update, "IdpSessionRepository.RemoveAccountAsync");
            return result?.ModifiedCount > 0;
        }

        public async Task<bool> UpdateActivityAsync(string sessionId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            var idleExpiry = DateTime.UtcNow.Add(GetIdpSessionIdleTimeout());
            var update = Builders<IdpSessionModel>.Update
                .Set(s => s.LastActivityAt, DateTime.UtcNow)
                .Set(s => s.IdleExpiry, idleExpiry);
            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.IdpSessionModel, filter, update, "IdpSessionRepository.UpdateActivityAsync");
            return result?.ModifiedCount > 0;
        }

        public async Task<bool> RevokeAsync(string sessionId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            var update = Builders<IdpSessionModel>.Update.Set(s => s.RevokedAt, DateTime.UtcNow);
            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.IdpSessionModel, filter, update, "IdpSessionRepository.RevokeAsync");
            return result?.ModifiedCount > 0;
        }

        public async Task<bool> DeleteAsync(string sessionId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.Eq(s => s.SessionId, sessionId);
            var update = Builders<IdpSessionModel>.Update.Set(s => s.RevokedAt, DateTime.UtcNow);
            var result = await _dataAccess.UpdateOneAsync(collection, IamSchemas.IdpSessionModel, filter, update, "IdpSessionRepository.DeleteAsync");
            return result?.ModifiedCount > 0;
        }

        public async Task<IEnumerable<IdpSessionModel>> GetByUserAsync(string userId, string tenantId)
        {
            var collection = GetDatabase().GetCollection<IdpSessionModel>("IdpSessions");
            var filter = Builders<IdpSessionModel>.Filter.And(
                Builders<IdpSessionModel>.Filter.ElemMatch(s => s.Accounts,
                    Builders<IdpSessionAccount>.Filter.Eq(a => a.UserId, userId)),
                Builders<IdpSessionModel>.Filter.Eq(s => s.TenantId, tenantId)
            );
            return await _dataAccess.FindListAsync(collection, IamSchemas.IdpSessionModel, filter, "IdpSessionRepository.GetByUserAsync");
        }

        private static TimeSpan GetIdpSessionIdleTimeout()
        {
            return SessionTimeoutConfig.GetIdleTimeout();
        }
    }
}