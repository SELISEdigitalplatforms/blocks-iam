using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blocks.CaptchaDriver;

/// <summary>
/// Resolves a reCAPTCHA configuration either from the secret store (database) or, as a fallback,
/// from local configuration. The factory never returns <c>null</c>.
/// </summary>
public sealed class RecaptchaConfigFactory : IRecaptchaConfigFactory
{
    private readonly ILogger<RecaptchaConfigFactory> _logger;
    private readonly ICaptchaConfigurationService _captchaConfigurationService;
    private readonly CaptchaOptions _options;

    public RecaptchaConfigFactory(
        ILogger<RecaptchaConfigFactory> logger,
        ICaptchaConfigurationService captchaConfigurationService,
        IOptions<CaptchaOptions> options)
    {
        _logger = logger;
        _captchaConfigurationService = captchaConfigurationService;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<IRecaptchaConfig> GetRecaptchaConfig(string? reCaptchaVerificationUriFormat, string? token)
    {
        if (reCaptchaVerificationUriFormat is null || token is null)
        {
            throw new ArgumentNullException(nameof(reCaptchaVerificationUriFormat));
        }

        try
        {
            var config = await _captchaConfigurationService.GetCaptchaConfigurationAsync();

            if (config is null || string.IsNullOrWhiteSpace(config.CaptchaSecret))
            {
                _logger.LogDebug("No reCAPTCHA config in store; using local config");
                return new LocalReCaptchaConfig(reCaptchaVerificationUriFormat, token);
            }

            return new DbReCaptchaConfig(config, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load reCAPTCHA config from store; falling back to local config");
            return new LocalReCaptchaConfig(reCaptchaVerificationUriFormat, token);
        }
    }

    /// <inheritdoc />
    public async Task<CaptchaConfiguration?> GetConfigFromDb()
    {
        return await _captchaConfigurationService.GetCaptchaConfigurationAsync();
    }
}
