namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves an <see cref="ICaptchaVerificationService"/> implementation by captcha provider name.
/// </summary>
public interface ICaptchaVerificationServiceProvider
{
    /// <summary>
    /// Returns the verification service registered for the given provider.
    /// </summary>
    /// <param name="provider">Provider key (e.g. <c>recaptcha</c>, <c>hcaptcha</c>, <c>bcaptcha</c>).</param>
    /// <exception cref="InvalidOperationException">Thrown when no service is registered for the provider.</exception>
    ICaptchaVerificationService GetCaptchaVerificationService(string provider);
}
