using MongoDB.Driver;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;

namespace Authentication.DomainService.Oidc.Repositories
{
    public class AuthorizationCodeRepository : IAuthorizationCodeRepository
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
            try
            {
                var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
                await collection.InsertOneAsync(code);
                _logger.LogInformation($"Authorization code created for user {code.UserId}, client {code.ClientId}");
                return code.Code;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating authorization code");
                throw;
            }
        }

        public async Task<AuthorizationCodeModel> GetByCodeAsync(string code)
        {
            try
            {
                var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
                var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
                return await collection.Find(filter).FirstOrDefaultAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error fetching authorization code: {code}");
                throw;
            }
        }

        public async Task<bool> MarkAsUsedAsync(string code, DateTime usedAt, string ipAddress)
        {
            try
            {
                var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
                var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
                var update = Builders<AuthorizationCodeModel>.Update
                    .Set(c => c.IsUsed, true)
                    .Set(c => c.UsedAt, usedAt)
                    .Set(c => c.UsedByIpAddress, ipAddress);

                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error marking authorization code as used: {code}");
                throw;
            }
        }

        public async Task<bool> DeleteAsync(string code)
        {
            try
            {
                var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
                var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
                var update = Builders<AuthorizationCodeModel>.Update
                    .Set(c => c.IsRevoked, true)
                    .Set(c => c.RevokedAt, DateTime.UtcNow);

                var result = await collection.UpdateOneAsync(filter, update);
                return result.ModifiedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error soft deleting authorization code: {code}");
                throw;
            }
        }

        public async Task<IEnumerable<AuthorizationCodeModel>> GetExpiredAsync()
        {
            try
            {
                var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
                var filter = Builders<AuthorizationCodeModel>.Filter.Lt(c => c.ExpiresAt, DateTime.UtcNow);
                return await collection.Find(filter).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching expired authorization codes");
                throw;
            }
        }
    }
}

