using Blocks.Genesis;
using MongoDB.Driver;

namespace Captcha.DomainService.Configuration
{
    public class CaptchaConfigurationRepository : ICaptchaConfigurationRepository
    {
        private readonly IDbContextProvider _dbContextProvider;
        private const string _collectionName = "Secret";

        public CaptchaConfigurationRepository(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public async Task<CaptchaConfiguration> GetByProviderAsync(string provider)
        {
            var collection = _dbContextProvider.GetCollection<Secret>("Secrets");
            var filter = Builders<Secret>.Filter.And(
                Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey),
                Builders<Secret>.Filter.Eq($"KeyPairs.{CaptchaSecretKeys.Provider}", provider));


            var secret = await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
            return MapToCaptchaConfiguration(secret);
        }

        public async Task<CaptchaConfiguration> GetCaptchaConfigurationAsync()
        {
            var collection = _dbContextProvider.GetCollection<Secret>("Secrets");
            var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

            var secrets = await (await collection.FindAsync(filter)).ToListAsync();

            var configuration = secrets.Select(MapToCaptchaConfiguration).FirstOrDefault(configuration => configuration is { IsEnable: true });
            return configuration is { IsEnable: true } ? configuration : null;
        }

        private static CaptchaConfiguration MapToCaptchaConfiguration(Secret secret)
        {
            if (secret?.KeyPairs is not { } values)
            {
                return null;
            }

            return new CaptchaConfiguration
            {
                CaptchaKey = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaKey),
                CaptchaSecret = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaSecret),
                Provider = values.GetValueOrDefault(CaptchaSecretKeys.Provider),
                CaptchaGenerator = values.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator),
                IsEnable = bool.TryParse(values.GetValueOrDefault(CaptchaSecretKeys.IsEnable), out var isEnable) && isEnable
            };
        }
    }
}
