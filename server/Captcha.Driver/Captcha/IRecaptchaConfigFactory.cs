namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves a reCAPTCHA configuration either from the secret store or local configuration.
/// </summary>
public interface IRecaptchaConfigFactory
{
    /// <summary>
    /// Returns a reCAPTCHA configuration for the supplied verification URI template and token.
    /// </summary>
    /// <param name="reCaptchaVerificationUriFormat">
    /// A composite format string with a single <c>{0}</c> placeholder for the token (used for the local fallback path).
    /// </param>
    /// <param name="token">The captcha token being verified.</param>
    Task<IRecaptchaConfig> GetRecaptchaConfig(string? reCaptchaVerificationUriFormat, string? token);

    /// <summary>
    /// Reads the captcha configuration from the secret store, or returns <c>null</c> if none is configured.
    /// </summary>
    Task<CaptchaConfiguration?> GetConfigFromDb();
}
