namespace Blocks.CaptchaDriver;

/// <summary>
/// Issues and verifies captcha verification codes.
/// </summary>
public interface ICaptchaProcessor
{
    /// <summary>
    /// Creates a one-time verification code, stores the originating host name against it in the cache,
    /// and removes the original captcha identifier.
    /// </summary>
    /// <param name="captchaId">Original captcha identifier returned to the client.</param>
    /// <param name="hostName">Host name of the client that submitted the captcha.</param>
    /// <returns>The opaque verification code the client must present to verify.</returns>
    Task<string> SubmitAndCreateVerificationCodeAsync(string? captchaId, string? hostName);

    /// <summary>
    /// Verifies a previously issued verification code against the provider-specific service.
    /// </summary>
    /// <param name="configProvider">Provider key (e.g. <c>bcaptcha</c>, <c>recaptcha</c>, <c>hcaptcha</c>).</param>
    /// <param name="verificationCode">Opaque verification code returned by <see cref="SubmitAndCreateVerificationCodeAsync"/>.</param>
    Task<VerificationResult> VerifyCaptchaAsync(string? configProvider, string? verificationCode);
}
