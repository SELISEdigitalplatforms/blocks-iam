using Blocks.Genesis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Reads captcha configuration, preferring the store blocks-os writes and falling back to the
/// legacy secret document.
/// </summary>
/// <remarks>
/// <para>
/// <b>Source A (current)</b> — the tenant's <c>keyValueStores</c> collection, keys prefixed
/// <c>captcha_</c>, written by blocks-os. The secret is not here; the record points at Key Vault
/// through <c>SecretId</c>.
/// </para>
/// <para>
/// <b>Source B (legacy)</b> — a single document in the tenant's <c>Secrets</c> collection with
/// <c>SecretKey = "captcha"</c>, carrying the secret inline. Kept so tenants configured before the
/// move keep working with no data change. Source A wins outright when both are populated: merging
/// them could pair a new site key with a stale secret and fail verification in a way that is very
/// hard to diagnose.
/// </para>
/// </remarks>
public sealed class CaptchaConfigurationRepository : ICaptchaConfigurationRepository
{
    private const string LegacyCollectionName = "Secrets";

    /// <summary>Key prefix blocks-os writes captcha configuration under.</summary>
    internal const string StoreKeyPrefix = "captcha_";

    private readonly IDbContextProvider _dbContextProvider;
    private readonly IKeyValueStore _keyValueStore;
    private readonly ILogger<CaptchaConfigurationRepository> _logger;

    public CaptchaConfigurationRepository(
        IDbContextProvider dbContextProvider,
        IKeyValueStore keyValueStore,
        ILogger<CaptchaConfigurationRepository> logger)
    {
        _dbContextProvider = dbContextProvider;
        _keyValueStore = keyValueStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetByProviderAsync(string? provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        var record = (await ReadStoreRecordsAsync().ConfigureAwait(false))
            .FirstOrDefault(r => string.Equals(r.Provider, provider, StringComparison.OrdinalIgnoreCase));

        if (record is not null)
        {
            return record.ToConfiguration();
        }

        // Deliberately not filtered on IsEnable: that is the pre-existing behaviour, and
        // CaptchaService.VerifyCaptchaAsync calls this with a provider already taken from the
        // enabled configuration.
        var collection = _dbContextProvider.GetCollection<Secret>(LegacyCollectionName);
        var filter = Builders<Secret>.Filter.And(
            Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey),
            Builders<Secret>.Filter.Eq($"KeyValuePairs.{CaptchaSecretKeys.Provider}", provider));

        var secret = await collection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
        return CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync()
    {
        var enabled = (await ReadStoreRecordsAsync().ConfigureAwait(false))
            .FirstOrDefault(r => r.IsEnable);

        if (enabled is not null)
        {
            return enabled.ToConfiguration();
        }

        var collection = _dbContextProvider.GetCollection<Secret>(LegacyCollectionName);
        var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

        var secret = await collection.Find(filter).FirstOrDefaultAsync().ConfigureAwait(false);
        var configuration = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
        return configuration is { IsEnable: true } ? configuration : null;
    }

    /// <summary>
    /// Reads every <c>captcha_*</c> record for the tenant, ordered deterministically.
    /// </summary>
    /// <remarks>
    /// Ordered by <c>Id</c>, which is equivalent to ordering by store key: the key is
    /// <c>captcha_</c> + <c>Id</c> and the prefix is constant. blocks-os enforces no
    /// single-enabled constraint, and <c>GetByPrefixAsync</c> applies no sort, so without this the
    /// winner among several enabled records could change between calls.
    /// <para>
    /// Records are read as raw documents and converted one at a time so a single malformed entry —
    /// written by another service — is skipped rather than failing an anonymous request. A failure
    /// of the read itself falls back to the legacy document for the same reason.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<CaptchaConfigRecord>> ReadStoreRecordsAsync()
    {
        List<BsonDocument> documents;

        try
        {
            documents = (await _keyValueStore
                .GetByPrefixAsync<BsonDocument>(StoreKeyPrefix)
                .ConfigureAwait(false)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading captcha configuration from the key/value store failed; falling back to the legacy secret document.");
            return [];
        }

        var records = new List<CaptchaConfigRecord>(documents.Count);

        foreach (var document in documents)
        {
            try
            {
                records.Add(BsonSerializer.Deserialize<CaptchaConfigRecord>(document));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping a malformed captcha configuration record.");
            }
        }

        return records.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
    }
}
