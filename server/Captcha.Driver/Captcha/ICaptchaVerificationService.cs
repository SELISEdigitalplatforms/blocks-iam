namespace Blocks.CaptchaDriver;

/// <summary>
/// Verifies a captcha verification code that was previously issued by the driver.
/// Implementations are selected by provider name (e.g. <c>bcaptcha</c>, <c>recaptcha</c>, <c>hcaptcha</c>).
/// </summary>
public interface ICaptchaVerificationService
{
    /// <summary>
    /// Provider identifier this service handles (e.g. <c>bcaptcha</c>).
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Verifies a previously issued verification code.
    /// </summary>
    /// <param name="verificationCode">Opaque verification code returned by the submit step.</param>
    /// <returns>Verification outcome including optional host name and any error details.</returns>
    Task<VerificationResult> VerifyAsync(string? verificationCode);
}
