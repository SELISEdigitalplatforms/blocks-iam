using Blocks.Genesis;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Read-only repository for captcha configuration entries stored in the secret store.
/// </summary>
public interface ICaptchaConfigurationRepository
{
    /// <summary>
    /// Returns the captcha configuration for a given provider key, or <c>null</c> when none is configured.
    /// </summary>
    /// <param name="provider">Provider identifier (e.g. <c>recaptcha</c>, <c>hcaptcha</c>).</param>
    Task<CaptchaConfiguration?> GetByProviderAsync(string? provider);

    /// <summary>
    /// Returns the currently active captcha configuration (i.e. <c>IsEnable = true</c>), or <c>null</c>.
    /// </summary>
    Task<CaptchaConfiguration?> GetCaptchaConfigurationAsync();
}
