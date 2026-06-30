namespace Blocks.CaptchaDriver
{
    public interface ICaptchaVerificationServiceProvider
    {
        ICaptchaVerificationService GetCaptchaVerificationService(string provider);
    }
}