namespace Blocks.CaptchaDriver;

/// <summary>
/// Builds the reCAPTCHA verification request for the tenant's active configuration.
/// </summary>
public interface IRecaptchaConfigFactory
{
    /// <summary>
    /// Returns the configuration to verify with, or <c>null</c> when the tenant has a captcha
    /// configuration whose secret cannot be obtained.
    /// </summary>
    /// <remarks>
    /// <c>null</c> means "do not call the provider" — verification fails closed. It is distinct
    /// from a tenant with no configuration at all, which still yields a local config so existing
    /// behaviour is unchanged.
    /// </remarks>
    Task<IRecaptchaConfig?> GetRecaptchaConfig(string? reCaptchaVerificationUriFormat, string? token);

    /// <summary>Returns the active captcha configuration from the store, if any.</summary>
    Task<CaptchaConfiguration?> GetConfigFromDb();
}
