namespace Blocks.CaptchaDriver;

/// <summary>
/// Service abstraction over the captcha configuration secret store.
/// </summary>
public interface ICaptchaConfigurationService
{
    /// <summary>
    /// Returns the currently active captcha configuration (i.e. <c>IsEnable = true</c>), or <c>null</c>.
    /// </summary>
    Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync();

    /// <summary>
    /// Returns the captcha configuration for a given provider name.
    /// </summary>
    /// <param name="configurationName">Provider identifier (e.g. <c>recaptcha</c>, <c>hcaptcha</c>).</param>
    Task<CaptchaConfiguration?> GetByNameAsync(string? configurationName);
}

