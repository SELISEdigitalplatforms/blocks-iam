using Blocks.Genesis;
using Moq;
using StackExchange.Redis;

namespace XUnitTest.TestSupport;

/// <summary>
/// A LOCAL throwaway test Redis (default <c>127.0.0.1:6379</c>, override with <c>BLOCKS_TEST_REDIS</c>,
/// e.g. <c>127.0.0.1:6390</c>). Each instance works under its own random tenant ids and deletes its
/// keys on dispose. Never point it at a real Redis.
/// </summary>
public sealed class RealRedis : IDisposable
{
    public const string EnvVar = "BLOCKS_TEST_REDIS";

    private static readonly Lazy<ConnectionMultiplexer?> Shared = new(Connect);
    private readonly List<string> _tenants = new();

    public static string Endpoint =>
        Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } v ? v : "127.0.0.1:6379";

    public static bool IsAvailable => Shared.Value != null;

    public IDatabase Database => Shared.Value!.GetDatabase();

    /// <summary>A fresh tenant id whose keys this instance removes on dispose.</summary>
    public string NewTenant()
    {
        var tenant = "T" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        _tenants.Add(tenant);
        return tenant;
    }

    /// <summary>An <see cref="ICacheClient"/> backed by this Redis (the members iam uses).</summary>
    public ICacheClient CacheClient() => CacheClientFor(Database);

    public static ICacheClient CacheClientFor(IDatabase db)
    {
        var cache = new Mock<ICacheClient>();
        cache.Setup(c => c.CacheDatabase()).Returns(db);
        cache.Setup(c => c.RemoveKeyAsync(It.IsAny<string>())).Returns<string>(k => db.KeyDeleteAsync(k));
        cache.Setup(c => c.KeyExistsAsync(It.IsAny<string>())).Returns<string>(k => db.KeyExistsAsync(k));
        cache.Setup(c => c.GetStringValueAsync(It.IsAny<string>()))
            .Returns<string>(async k => (string?)await db.StringGetAsync(k) ?? null!);
        cache.Setup(c => c.AddStringValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>()))
            .Returns<string, string, long>((k, v, ttl) => db.StringSetAsync(k, v, TimeSpan.FromSeconds(ttl)));
        return cache.Object;
    }

    /// <summary>A database whose server never answers: every call fails like a Redis outage.</summary>
    public static IDatabase Unreachable() =>
        ConnectionMultiplexer.Connect(new ConfigurationOptions
        {
            EndPoints = { "127.0.0.1:1" },
            AbortOnConnectFail = false,
            ConnectTimeout = 300,
            SyncTimeout = 300,
            AsyncTimeout = 300,
            ConnectRetry = 0
        }).GetDatabase();

    public void Dispose()
    {
        if (!IsAvailable)
        {
            return;
        }

        var server = Shared.Value!.GetServers()[0];
        foreach (var tenant in _tenants)
        {
            foreach (var key in server.Keys(pattern: $"iam:{tenant}:*"))
            {
                Database.KeyDelete(key);
            }
        }
    }

    private static ConnectionMultiplexer? Connect()
    {
        try
        {
            var mux = ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { Endpoint },
                AbortOnConnectFail = true,
                ConnectTimeout = 2000,
                // The full suite runs many classes in parallel; a busy thread pool must not turn into
                // a false Redis timeout.
                SyncTimeout = 15000,
                AsyncTimeout = 15000,
                AllowAdmin = false
            });
            return mux.GetDatabase().Ping() >= TimeSpan.Zero ? mux : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>A fact that runs only when the local test Redis answers; otherwise it is skipped with a reason.</summary>
public sealed class RealRedisFactAttribute : FactAttribute
{
    public RealRedisFactAttribute()
    {
        if (!RealRedis.IsAvailable)
        {
            Skip = $"No test Redis at {RealRedis.Endpoint} (docker start blocks-test-redis).";
        }
    }
}

/// <summary>A fact that needs both the local test Redis and the local test Mongo.</summary>
public sealed class RealRedisAndMongoFactAttribute : FactAttribute
{
    public RealRedisAndMongoFactAttribute()
    {
        if (!RealRedis.IsAvailable || !RealMongo.IsAvailable)
        {
            Skip = $"Needs test Redis at {RealRedis.Endpoint} and test Mongo at {RealMongo.ConnectionString}.";
        }
    }
}
