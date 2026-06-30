namespace Blocks.CaptchaDriver
{
    public interface ICaptchaConfigurationService
    {
        Task<CaptchaConfiguration> GetCaptchaConfigurationAsync();
        Task<CaptchaConfiguration> GetByNameAsync(string configurationName);
    }
}