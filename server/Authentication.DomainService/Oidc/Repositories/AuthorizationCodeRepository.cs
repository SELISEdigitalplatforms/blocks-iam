using MongoDB.Driver;
using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;

namespace Authentication.DomainService.Oidc.Repositories
{
    /// <summary>
    /// Read-only MongoDB fallback for codes written by older pods during a rolling deploy. New codes
    /// live in Redis (<see cref="RedisAuthorizationCodeStore"/>); nothing writes new rows here.
    /// </summary>
    public sealed class AuthorizationCodeRepository
    {
        private readonly IDbContextProvider _dbContextProvider;

        public AuthorizationCodeRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        private IMongoDatabase GetDatabase() =>
            _dbContextProvider.GetDatabase()
            ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

        public async Task<AuthorizationCodeModel> GetByCodeAsync(string code)
        {
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            var filter = Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code);
            return await collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<bool> TryConsumeAsync(string code)
        {
            // One atomic update: the filter only matches a code nobody has used yet, so two parallel
            // exchanges of the same code cannot both succeed (RFC 9700 4.2.4: codes are single use).
            var collection = GetDatabase().GetCollection<AuthorizationCodeModel>("IdpAuthorizationCodes");
            var filter = Builders<AuthorizationCodeModel>.Filter.And(
                Builders<AuthorizationCodeModel>.Filter.Eq(c => c.Code, code),
                Builders<AuthorizationCodeModel>.Filter.Ne(c => c.IsRevoked, true));
            var update = Builders<AuthorizationCodeModel>.Update
                .Set(c => c.IsRevoked, true)
                .Set(c => c.RevokedAt, DateTime.UtcNow);

            var result = await collection.UpdateOneAsync(filter, update);
            return result.ModifiedCount == 1;
        }
    }
}
