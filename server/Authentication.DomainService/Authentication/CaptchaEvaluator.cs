using Authentication.DomainService.Oidc.Repositories;
using Blocks.CaptchaDriver;

namespace Authentication.DomainService.Authentication
{
    public sealed class CaptchaEvaluator : ICaptchaEvaluator
    {
        private readonly ICaptchaService _captchaService;
        private readonly ICaptchaConfigurationService _captchaConfigurationService;

        public CaptchaEvaluator(ICaptchaService captchaService, ICaptchaConfigurationService captchaConfigurationService)
        {
            _captchaService = captchaService;
            _captchaConfigurationService = captchaConfigurationService;
        }

        public Task<CaptchaConfiguration?> GetConfigurationAsync()
            => _captchaConfigurationService.GetCaptchaConfigurationAsync();

        public async Task<object> VerifyAsync(string captchaCode, string configurationName)
        {
            var response = await _captchaService.VerifyCaptchaAsync(new VerifyCaptchaRequest
            {
                VerificationCode = captchaCode,
                ConfigurationName = configurationName
            });
            return new { Verified = response.Verified };
        }
    }
}
