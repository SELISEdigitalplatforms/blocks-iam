using Blocks.Genesis;

namespace Blocks.CaptchaDriver
{
    public class CaptchaProcessor : ICaptchaProcessor
    {
        private readonly ICacheClient _cache;
        private readonly ICaptchaVerificationServiceProvider _captchaVerificationServiceProvider;

        public CaptchaProcessor(ICacheClient cache,
                ICaptchaVerificationServiceProvider captchaVerificationServiceProvider)
        {
            _cache = cache;
            _captchaVerificationServiceProvider = captchaVerificationServiceProvider;
        }

        public async Task<string> SubmitAndCreateVerificationCodeAsync(string captchaId)
        {
            var verificationCode = Guid.NewGuid().ToString("n");
            var hostName = "abc.com";
            await _cache.AddStringValueAsync(verificationCode, hostName, 10 * 60);
            await _cache.RemoveKeyAsync(captchaId);

            return verificationCode;
        }

        public async Task<VerificationResult> VerifyCaptchaAsync(string configProvider, string verificationCode)
        {
            var captchaVerificationHandler = _captchaVerificationServiceProvider.GetCaptchaVerificationService(configProvider);
            return await captchaVerificationHandler.VerifyAsync(verificationCode);
        }
    }
}