namespace Blocks.CaptchaDriver
{
    public interface IRecaptchaConfigFactory
    {
        Task<IRecaptchaConfig> GetRecaptchaConfig(
               string reCaptchaVerificationUriFormat,
               string token);

        Task<CaptchaConfiguration> GetConfigFromDb();
    }
}