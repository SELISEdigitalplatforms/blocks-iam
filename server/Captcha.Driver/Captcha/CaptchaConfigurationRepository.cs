using Blocks.Genesis;
using MongoDB.Driver;

namespace Blocks.CaptchaDriver
{
    public class CaptchaConfigurationRepository : ICaptchaConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "Secrets";

        public CaptchaConfigurationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<CaptchaConfiguration> GetByProviderAsync(string provider)
        {
            var collection = _dbContextProvider.GetCollection<Secret>(_collectionName);
            var filter = Builders<Secret>.Filter.And(
                Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey),
                Builders<Secret>.Filter.Eq($"KeyValuePairs.{CaptchaSecretKeys.Provider}", provider));


            var secret = await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
            return CaptchaConfigurationMapping.MapToCaptchaConfiguration(secret);
        }

        public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync()
        {
            var collection = _dbContextProvider.GetCollection<Secret>(_collectionName);
            var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

            var secrets = await (await collection.FindAsync(filter)).ToListAsync();

            var configuration = secrets.Select(CaptchaConfigurationMapping.MapToCaptchaConfiguration).FirstOrDefault(configuration => configuration is { IsEnable: true });
            return configuration is { IsEnable: true } ? configuration : null;
        }
    }
}