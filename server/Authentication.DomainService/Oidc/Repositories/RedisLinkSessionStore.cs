using System.Text.Json;
using Blocks.Genesis;
using Iam.DomainService.Utilities;
using Idp.DomainService.Oidc.Contracts;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Authentication.DomainService.Oidc.Repositories;

/// <summary>
/// Signup-link sessions in Redis: key <c>iam:&lt;tenantId&gt;:linksession:&lt;sha256hex(sessionId)&gt;</c>,
/// value + 30 min TTL in one command. Read many times; deleted on password login or logout.
/// Sessions of older pods are still read from MongoDB during the rollout. A Redis error is never read
/// as "not found": it fails the request.
/// </summary>
public sealed class RedisLinkSessionStore : ILinkSessionRepository
{
    public static readonly TimeSpan KeyTtl = TimeSpan.FromMinutes(IdpConstants.LinkSessionLifetimeMinutes);

    private readonly ICacheClient _cacheClient;
    private readonly LinkSessionRepository _mongoFallback;
    private readonly ILogger<RedisLinkSessionStore> _logger;

    public RedisLinkSessionStore(
        ICacheClient cacheClient,
        LinkSessionRepository mongoFallback,
        ILogger<RedisLinkSessionStore> logger)
    {
        _cacheClient = cacheClient;
        _mongoFallback = mongoFallback;
        _logger = logger;
    }

    public async Task CreateAsync(LinkSessionModel session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var key = IamRedisKeys.LinkSession(session.TenantId, session.SessionId);
        if (!await _cacheClient.CacheDatabase().StringSetAsync(key, Serialize(session), KeyTtl))
        {
            throw new InvalidOperationException("Link session could not be stored.");
        }
        _logger.LogInformation("Link session created");
    }

    public async Task<LinkSessionModel?> GetBySessionIdAsync(string tenantId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var value = await _cacheClient.CacheDatabase().StringGetAsync(IamRedisKeys.LinkSession(tenantId, sessionId));
        if (value.HasValue)
        {
            var session = JsonSerializer.Deserialize<LinkSessionModel>(value.ToString())!;
            session.SessionId = sessionId;
            return session;
        }

        return await _mongoFallback.GetBySessionIdAsync(sessionId);
    }

    /// <summary>The stored JSON never holds the session id (the key holds only its hash).</summary>
    private static string Serialize(LinkSessionModel session)
    {
        var node = JsonSerializer.SerializeToNode(session)!.AsObject();
        node.Remove(nameof(LinkSessionModel.SessionId));
        return node.ToJsonString();
    }

    public async Task DeleteAsync(string tenantId, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _cacheClient.CacheDatabase().KeyDeleteAsync(IamRedisKeys.LinkSession(tenantId, sessionId));
        await _mongoFallback.DeleteAsync(sessionId);
    }
}
