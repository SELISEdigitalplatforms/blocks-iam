using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Repositories;

/// <summary>
/// MongoDB fallback for link sessions written by older pods during a rolling deploy: read, and delete
/// on logout / password login. New sessions live in Redis (<see cref="RedisLinkSessionStore"/>).
/// A delete on a dropped collection does not create it.
/// </summary>
public sealed class LinkSessionRepository
{
    public const string CollectionName = "LinkSessions";

    private readonly IDbContextProvider _dbContextProvider;

    public LinkSessionRepository(IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    private IMongoDatabase GetDatabase() =>
        _dbContextProvider.GetDatabase()
        ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

    private IMongoCollection<LinkSessionModel> Collection =>
        GetDatabase().GetCollection<LinkSessionModel>(CollectionName);

    public async Task<LinkSessionModel?> GetBySessionIdAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var filter = Builders<LinkSessionModel>.Filter.Eq(x => x.SessionId, sessionId);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task DeleteAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await Collection.DeleteOneAsync(Builders<LinkSessionModel>.Filter.Eq(x => x.SessionId, sessionId));
    }
}
