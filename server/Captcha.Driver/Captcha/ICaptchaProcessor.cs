namespace Blocks.CaptchaDriver
{
    public interface ICaptchaProcessor
    {
        Task<string> SubmitAndCreateVerificationCodeAsync(string captchaId);
        Task<VerificationResult> VerifyCaptchaAsync(string configProvider, string verificationCode);
    }
}