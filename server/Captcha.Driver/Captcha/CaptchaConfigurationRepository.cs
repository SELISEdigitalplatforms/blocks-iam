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

    /// <summary>
    /// The single key every captcha configuration is stored under.
    /// </summary>
    /// <remarks>
    /// Records are told apart by the store's own <c>ItemId</c>, not by the key, so the key
    /// carries no identity and never varies. This mirrors blocks-os's writer, which uses the
    /// multi-value side of <see cref="IKeyValueStore"/> (<c>AddAsync</c> / <c>GetAllAsync</c> /
    /// the <c>*ById</c> methods). Reading with the single-value <c>GetAsync</c> would return an
    /// arbitrary one of the records.
    /// </remarks>
    internal const string StoreKey = "captcha";

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
    /// Reads every captcha record for the tenant, ordered deterministically.
    /// </summary>
    /// <remarks>
    /// Ordered by the store's <c>ItemId</c>. blocks-os enforces no single-enabled constraint and
    /// <c>GetAllAsync</c> applies no sort, so without this the winner among several enabled
    /// records could change between calls. <c>ItemId</c> is used rather than anything in the
    /// payload because blocks-os deliberately does not persist an id inside the value — the store
    /// entry is the identity — so every record's payload id would otherwise be empty and the
    /// ordering would collapse back to Mongo's natural order.
    /// <para>
    /// Records are read as raw documents and converted one at a time so a single malformed entry —
    /// written by another service — is skipped rather than failing an anonymous request. A failure
    /// of the read itself falls back to the legacy document for the same reason.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<CaptchaConfigRecord>> ReadStoreRecordsAsync()
    {
        IReadOnlyList<KeyValueItem<BsonDocument>> items;

        try
        {
            items = await _keyValueStore
                .GetAllAsync<BsonDocument>(StoreKey)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reading captcha configuration from the key/value store failed; falling back to the legacy secret document.");
            return [];
        }

        var records = new List<CaptchaConfigRecord>(items.Count);

        foreach (var item in items.OrderBy(i => i.ItemId, StringComparer.Ordinal))
        {
            try
            {
                var record = BsonSerializer.Deserialize<CaptchaConfigRecord>(item.Value);

                // The identity lives on the store entry, not in the stored payload.
                record.Id = item.ItemId;
                records.Add(record);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping a malformed captcha configuration record.");
            }
        }

        return records;
    }
}
