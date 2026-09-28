using Blocks.Genesis;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Authentication.DomainService.Oidc.Repositories;

public sealed class LinkSessionRepository : ILinkSessionRepository
{
    public const string CollectionName = "LinkSessions";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly ILogger<LinkSessionRepository> _logger;

    public LinkSessionRepository(
        IDbContextProvider dbContextProvider,
        ILogger<LinkSessionRepository> logger)
    {
        _dbContextProvider = dbContextProvider;
        _logger = logger;
    }

    private IMongoDatabase GetDatabase() =>
        _dbContextProvider.GetDatabase()
        ?? throw new InvalidOperationException("No active MongoDB database is available in current Genesis context.");

    private IMongoCollection<LinkSessionModel> Collection =>
        GetDatabase().GetCollection<LinkSessionModel>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct = default)
    {
        try
        {
            var keys = Builders<LinkSessionModel>.IndexKeys;
            await Collection.Indexes.CreateOneAsync(
                new CreateIndexModel<LinkSessionModel>(
                    keys.Ascending(x => x.ExpiresAtUtc),
                    new CreateIndexOptions { Name = "ix_expires", ExpireAfter = TimeSpan.Zero }),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LinkSessionRepository.EnsureIndexesAsync failed");
        }
    }

    public async Task CreateAsync(LinkSessionModel session)
    {
        await Collection.InsertOneAsync(session);
        _logger.LogInformation("Link session created for user {UserId} link {LinkId}", session.UserId, session.LinkId);
    }

    public async Task<LinkSessionModel?> GetBySessionIdAsync(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var filter = Builders<LinkSessionModel>.Filter.Eq(x => x.SessionId, sessionId);
        return await Collection.Find(filter).FirstOrDefaultAsync();
    }
}
