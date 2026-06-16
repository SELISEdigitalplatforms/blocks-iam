using Blocks.Genesis;
using MongoDB.Driver;
using Captcha.DomainService.Configuration;

namespace Captcha.DomainService.Captcha
{
    public class CaptchaGeneratorProvider : ICaptchaGeneratorProvider
    {
        private readonly string _collectionName = "Secret";
        private readonly IDbContextProvider _dbContextProvider;

        private static readonly IDictionary<string, ICaptchaGenerator> CaptchaGenerators = new Dictionary<string, ICaptchaGenerator>
        {
            { nameof(EasyCaptchaGenerator).ToLower(), new EasyCaptchaGenerator() },
            { nameof(HardCaptchaGenerator).ToLower(), new HardCaptchaGenerator() }
        };

        public CaptchaGeneratorProvider(IDbContextProvider dbContextProvider)
        {
            _dbContextProvider = dbContextProvider;
        }

        public ICaptchaGenerator GetCaptchaGenerator(string configurationName)
        {
            string generatorName = GetGeneratorName(configurationName);
            return CaptchaGenerators[generatorName];
        }

        public virtual string GetGeneratorName(string configurationName)
        {
            var collection = _dbContextProvider.GetCollection<Secret>(_collectionName);
            var filter = Builders<Secret>.Filter.And(
                Builders<Secret>.Filter.Eq(s => s.SecretKey, CaptchaSecretKeys.SecretKey),
                Builders<Secret>.Filter.Eq($"KeyValuePairs.{CaptchaSecretKeys.Provider}", configurationName));
            var setting = collection.Find(filter).FirstOrDefault();
            var captchaGenerator = setting?.KeyValuePairs?.GetValueOrDefault(CaptchaSecretKeys.CaptchaGenerator);
            var generatorName = string.IsNullOrWhiteSpace(captchaGenerator) ? nameof(HardCaptchaGenerator) : captchaGenerator;

            return generatorName.ToLower();
        }
    }
}
