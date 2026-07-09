using Blocks.Genesis;
using MongoDB.Driver;

namespace Blocks.CaptchaDriver;

/// <summary>
/// MongoDB-backed repository for captcha configuration. Reads from the <c>Secrets</c>
/// collection using the canonical <see cref="CaptchaSecretKeys.SecretKey"/> key.
/// </summary>
public sealed class CaptchaConfigurationRepository : ICaptchaConfigurationRepository
{
    private const string CollectionName = "Secrets";

    private readonly IDbContextProvider _dbContextProvider;

    public CaptchaConfigurationRepository(IDbContextProvider dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetByProviderAsync(string? provider)
    {
        if (provider is null)
        {
            throw new ArgumentNullException(nameof(provider));
        }

        var collection = _dbContextProvider.GetCollection<Secret>(CollectionName);
        var filter = Builders<Secret>.Filter.And(
            Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey),
            Builders<Secret>.Filter.Eq($"KeyValuePairs.{CaptchaSecretKeys.Provider}", provider));

        var secret = await collection.Find(filter).FirstOrDefaultAsync();
        return CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync()
    {
        var collection = _dbContextProvider.GetCollection<Secret>(CollectionName);
        var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

        var secret = await collection.Find(filter).FirstOrDefaultAsync();
        var configuration = CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
        return configuration is { IsEnable: true } ? configuration : null;
    }
}
