using Blocks.Genesis;
using MongoDB.Driver;

namespace XUnitTest.TestSupport;

/// <summary>
/// A throwaway database on a LOCAL test Mongo (default <c>mongodb://127.0.0.1:27017</c>, override with
/// <c>BLOCKS_TEST_MONGO</c>). Each instance owns one uniquely named database and drops it on dispose.
/// Never point it at a real database.
/// </summary>
public sealed class RealMongo : IDisposable
{
    public const string EnvVar = "BLOCKS_TEST_MONGO";

    private static readonly Lazy<bool> Reachable = new(Probe);

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable(EnvVar) is { Length: > 0 } v ? v : "mongodb://127.0.0.1:27017";

    public static bool IsAvailable => Reachable.Value;

    private readonly IMongoClient _client;

    public RealMongo()
    {
        _client = new MongoClient(ConnectionString);
        Database = _client.GetDatabase("iam_test_" + Guid.NewGuid().ToString("n"));
    }

    public IMongoDatabase Database { get; }

    /// <summary>A provider whose context database is this one, for any document type.</summary>
    public IDbContextProvider ProviderFor() => new Context(Database);

    public void Dispose() => _client.DropDatabase(Database.DatabaseNamespace.DatabaseName);

    private static bool Probe()
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            new MongoClient(settings).GetDatabase("admin").RunCommand<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed class Context(IMongoDatabase database) : IDbContextProvider
    {
        public IMongoDatabase GetDatabase(string tenantId) => database;
        public IMongoDatabase? GetDatabase() => database;
        public IMongoDatabase GetDatabase(string connectionString, string databaseName, bool isCacheRefreshed = false) => database;
        public IMongoCollection<T> GetCollection<T>(string collectionName) => database.GetCollection<T>(collectionName);
        public IMongoCollection<T> GetCollection<T>(string tenantId, string collectionName) => database.GetCollection<T>(collectionName);
    }
}

/// <summary>A fact that runs only when the local test Mongo answers; otherwise it is skipped with a reason.</summary>
public sealed class RealMongoFactAttribute : FactAttribute
{
    public RealMongoFactAttribute()
    {
        if (!RealMongo.IsAvailable)
        {
            Skip = $"No test Mongo at {RealMongo.ConnectionString} (docker start blocks-test-mongo).";
        }
    }
}
