namespace Blocks.CaptchaDriver
{
    public interface ICaptchaService
    {
        Task<SubmitCaptchaRequestResponse> SubmitCaptchaAsync(SubmitCaptchaRequest command);
        Task<VerifyCaptchaRequestResponse> VerifyCaptchaAsync(VerifyCaptchaRequest query);
    }
}