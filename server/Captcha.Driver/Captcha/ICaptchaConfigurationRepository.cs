namespace Blocks.CaptchaDriver
{
    public interface ICaptchaConfigurationRepository
    {
        Task<CaptchaConfiguration> GetByProviderAsync(string provider);
        Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync();
    }
}