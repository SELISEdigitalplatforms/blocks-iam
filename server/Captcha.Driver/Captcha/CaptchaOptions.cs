namespace Blocks.CaptchaDriver;

/// <summary>
/// Strongly-typed configuration for the Captcha driver.
/// Bound from the <c>Captcha</c> configuration section.
/// </summary>
public class CaptchaOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "Captcha";

    /// <summary>
    /// Time-to-live, in seconds, for verification codes stored in cache. Default: 600 (10 minutes).
    /// </summary>
    public int VerificationCodeTtlSeconds { get; set; } = 600;

    /// <summary>
    /// Default verification URL used by the in-memory (BCaptcha) flow.
    /// </summary>
    public string BcaptchaVerificationUrl { get; set; } = string.Empty;

    /// <summary>
    /// Google reCAPTCHA verification endpoint.
    /// </summary>
    public string RecaptchaVerificationUrl { get; set; } =
        "https://www.google.com/recaptcha/api/siteverify";

    /// <summary>
    /// hCaptcha verification endpoint.
    /// </summary>
    public string HcaptchaVerificationUrl { get; set; } =
        "https://api.hcaptcha.com/siteverify";

    /// <summary>
    /// Legacy/optional Google secret key. Prefer storing secrets in the secret store
    /// (collection <c>Secrets</c>, key <c>captcha</c>) and reading via
    /// <see cref="ICaptchaConfigurationService"/>.
    /// </summary>
    public string? ReCaptchaSecretKey { get; set; }
}
