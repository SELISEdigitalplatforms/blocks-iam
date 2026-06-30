using MongoDB.Driver;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Repositories
{
    public sealed class AuthorizationCodeRepository : IAuthorizationCodeRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private readonly ILogger<AuthorizationCodeRepository> _logger;

        public AuthorizationCodeRepository(
            IDbContextProvider dbContextProvider,
            ILogger<AuthorizationCodeRepository> logger)
        {
            _dbContextProvider = dbContextProvider;
            _logger = logger;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<string> CreateAsync(AuthorizationCodeModel code)
        {
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            await collection.InsertOneAsync(code);
            _logger.LogInformation("Authorization code created for user {UserId}, client {ClientId}", code.UserId, code.ClientId);
            return code.Code;
        }

        public async Task<AuthorizationCodeModel> GetByCodeAsync(string code)
        {
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> DeleteAsync(string code)
        {
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
            var update = Builders<AuthorizationCodeModel>.Update
                .Set(c => c.IsRevoked, true)
                .Set(c => c.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }

        public async Task<IEnumerable<AuthorizationCodeModel>> GetExpiredAsync()
        {
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            var filter = Builders<AuthorizationCodeModel>.Filter.Lt(c => c.ExpiresAt, DateTime.UtcNow);
            return await collection.Find(filter).ToListAsync();
        }
    }
}