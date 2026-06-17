using Blocks.Genesis;
using MongoDB.Driver;
using Authentication.DomainService.Utilities;
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
                Builders<Secret>.Filter.Eq($"KeyValuePairs.{CaptchaSecretKeys.Provider}", provider));


            var secret = await (await collection.FindAsync(filter)).FirstOrDefaultAsync();
            return IdpConstants.MapToCaptchaConfiguration(secret);
        }

        public async Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync()
        {
            var collection = _dbContextProvider.GetCollection<Secret>("Secrets");
            var filter = Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey);

            var secrets = await (await collection.FindAsync(filter)).ToListAsync();

            var configuration = secrets.Select(IdpConstants.MapToCaptchaConfiguration).FirstOrDefault(configuration => configuration is { IsEnable: true });
            return configuration is { IsEnable: true } ? configuration : null;
        }
    }
}
